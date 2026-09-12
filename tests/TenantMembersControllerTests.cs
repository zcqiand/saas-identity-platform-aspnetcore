using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using PersistDb = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbUser = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysUser;
using DbMember = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantMember;
using DbRole = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysRole;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// M00.F02 — 成员六端点扁平 TenantMemberUserView（ADR-0032）+ invitation 嵌套 TenantMemberView（I42 方案 C）
/// + status 枚举显式映射（DB 0=disabled/1=active/2=invited/3=suspended ↔ 生成枚举 Active=0/Invited=1/Suspended=2/Disabled=3）。
/// </summary>
public class TenantMembersControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (PersistDb.AppDbContext db, TenantMembersController ctrl) Make(string name)
    {
        var options = new DbContextOptionsBuilder<PersistDb.AppDbContext>()
            .UseInMemoryDatabase(name).Options;
        var db = new PersistDb.AppDbContext(options);
        var guard = new TenantGuard(new StubTenantContext { TenantId = TenantId.ToString() });
        var ctrl = new TenantMembersController(guard, db, new NoopAuditWriter(), new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext(),
        });
        return (db, ctrl);
    }

    private static DbUser MakeUser(string name, short status = 1) => new()
    {
        Id = Guid.NewGuid(), Username = name, Email = $"{name}@x.com", Password = "p",
        Status = status, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static DbMember MakeMember(Guid userId, short status = 1) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, UserId = userId, MemberName = "m",
        Status = status, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    [Trait("Fn", "M00.F02.I06")]
    public async Task Invitations_createsInvitedUserAndActiveMember_nestedViewUnchanged()
    {
        var (db, ctrl) = Make("invite-1");
        var email = "newbie@example.com";
        var dto = await ctrl.Invitations(TenantId.ToString(), new Body3 { Email = email });

        // I42 oracle：user.status="invited"，member.status="active"，memberName=username
        // ADR-0032：invitations 保持嵌套 TenantMemberView 不动
        Assert.Equal(SysUserStatus.Invited, dto.User.Status);
        Assert.Equal(TenantMemberStatus.Active, dto.Member.Status);
        Assert.Equal(email, dto.User.Email);
        Assert.Equal(email, dto.Member.MemberName);
        Assert.Equal(email, dto.User.Username);

        // 真 user 行落库（DB smallint 2=invited）
        var user = await db.SysUsers.SingleAsync(u => u.Email == email);
        Assert.Equal((short)2, user.Status);
        var member = await db.TenantMembers.SingleAsync(m => m.UserId == user.Id);
        Assert.Equal((short)1, member.Status);
    }

    [Fact]
    [Trait("Fn", "M00.F02.I01")]
    public async Task MembersGet_returnsFlatView_addressableByUserId()
    {
        var (db, ctrl) = Make("members-flat");
        var u = MakeUser("alice");
        db.SysUsers.Add(u);
        var m = MakeMember(u.Id, status: 1);
        var role = new DbRole
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ClientId = "lab-management",
            RoleCode = "tenant_admin", RoleName = "管理员", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.SysRoles.Add(role);
        db.TenantMembers.Add(m);
        await db.SaveChangesAsync();
        m.Roles.Add(role);
        await db.SaveChangesAsync();

        // 单查：路径是 sys_user.id，响应 id = sys_user.id（ADR-0032 扁平）
        var dto = await ctrl.MembersGet(TenantId.ToString(), u.Id.ToString());
        Assert.Equal(u.Id, dto.Id);
        Assert.Equal(TenantId, dto.TenantId);
        Assert.Equal("alice", dto.Username);
        Assert.Equal(TenantMemberStatus.Active, dto.Status);
        Assert.Equal(role.Id.ToString(), Assert.Single(dto.RoleIds));

        var page = await ctrl.MembersGet(TenantId.ToString(), 0, 20, null);
        var item = Assert.Single(page.Items);
        Assert.Equal(u.Id, item.Id);
        Assert.Equal(role.Id.ToString(), Assert.Single(item.RoleIds));
    }

    [Fact]
    [Trait("Fn", "M00.F02.I01")]
    public async Task MembersGet_roleIds_returnJoinRowsVerbatim_crossTenantRoleIncluded()
    {
        var (db, ctrl) = Make("members-role-scope");
        var u = MakeUser("bob");
        db.SysUsers.Add(u);
        var m = MakeMember(u.Id);
        var inTenant = new DbRole
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ClientId = "lab-management",
            RoleCode = "r1", RoleName = "r1", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var otherTenant = new DbRole
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ClientId = "lab-management",
            RoleCode = "r2", RoleName = "r2", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.SysRoles.AddRange(inTenant, otherTenant);
        db.TenantMembers.Add(m);
        await db.SaveChangesAsync();
        m.Roles.Add(inTenant);
        m.Roles.Add(otherTenant);
        await db.SaveChangesAsync();

        var dto = await ctrl.MembersGet(TenantId.ToString(), u.Id.ToString());
        // 2026-09-12 对齐 msw oracle（I03/I04）：roleIds = join 行原样，跨租户 assignment
        // 不被 sys_role.tenant_id 过滤吞掉（seed 实例：member c00000000006 @tenant2 挂
        // role a00000000002 @tenant1，oracle 原样返回）。
        Assert.Equal(2, dto.RoleIds.Count);
        Assert.Contains(inTenant.Id.ToString(), dto.RoleIds);
        Assert.Contains(otherTenant.Id.ToString(), dto.RoleIds);
    }

    [Fact]
    [Trait("Fn", "M00.F02.I01")]
    public async Task MembersGet_mapsDbStatusToEnum_notRawCast()
    {
        var (db, ctrl) = Make("members-map");
        var u1 = MakeUser("u1");
        db.SysUsers.Add(u1);
        db.TenantMembers.Add(MakeMember(u1.Id, status: 2)); // DB invited
        var u2 = MakeUser("u2");
        db.SysUsers.Add(u2);
        db.TenantMembers.Add(MakeMember(u2.Id, status: 3)); // DB suspended（ADR-0032 4 值）
        await db.SaveChangesAsync();

        var page = await ctrl.MembersGet(TenantId.ToString(), 0, 20, null);
        Assert.Equal(2, page.Total);
        // 旧裸 cast 会把 DB 2 错映射成 Active —— 显式映射后必须是 Invited
        Assert.Contains(page.Items, i => i.Status == TenantMemberStatus.Invited);
        Assert.Contains(page.Items, i => i.Status == TenantMemberStatus.Suspended);
    }

    [Fact]
    [Trait("Fn", "M00.F02.I01")]
    public async Task MembersGet_statusFilter_usesDbValueNotEnumNumber()
    {
        var (db, ctrl) = Make("members-filter");
        var u = MakeUser("u1");
        db.SysUsers.Add(u);
        db.TenantMembers.Add(MakeMember(u.Id, status: 1));
        await db.SaveChangesAsync();

        // 生成枚举 Active=0，DB active=1 —— 过滤必须换算，不能 (short)enum
        var active = await ctrl.MembersGet(TenantId.ToString(), 0, 20, TenantMemberStatus.Active);
        Assert.Equal(1, active.Total);
        var disabled = await ctrl.MembersGet(TenantId.ToString(), 0, 20, TenantMemberStatus.Disabled);
        Assert.Equal(0, disabled.Total);
    }

    [Fact]
    [Trait("Fn", "M00.F02.I08")]
    public async Task Status_endpoint_writesDbValueNotEnumNumber()
    {
        var (db, ctrl) = Make("members-status");
        var uid = Guid.NewGuid();
        var u = MakeUser("u1");
        u.Id = uid;
        db.SysUsers.Add(u);
        db.TenantMembers.Add(MakeMember(uid, status: 1));
        await db.SaveChangesAsync();

        await ctrl.Status(TenantId.ToString(), uid.ToString(), new Body4 { Status = TenantMemberStatus.Disabled });
        var m = await db.TenantMembers.FirstAsync(x => x.UserId == uid);
        Assert.Equal((short)0, m.Status); // DB disabled=0，不是枚举编号 3
    }

    [Fact]
    [Trait("Fn", "M00.F02.I08")]
    public async Task Status_endpoint_suspended_mapsToDb3_andRoundTrips()
    {
        var (db, ctrl) = Make("members-suspend");
        var uid = Guid.NewGuid();
        var u = MakeUser("u1");
        u.Id = uid;
        db.SysUsers.Add(u);
        db.TenantMembers.Add(MakeMember(uid, status: 1));
        await db.SaveChangesAsync();

        // suspended → DB 3（ADR-0032 4 值码表；反向 ToDb 同步）
        var dto = await ctrl.Status(TenantId.ToString(), uid.ToString(), new Body4 { Status = TenantMemberStatus.Suspended });
        Assert.Equal(TenantMemberStatus.Suspended, dto.Status);
        Assert.Equal((short)3, (await db.TenantMembers.FirstAsync(x => x.UserId == uid)).Status);

        // 回 active
        var back = await ctrl.Status(TenantId.ToString(), uid.ToString(), new Body4 { Status = TenantMemberStatus.Active });
        Assert.Equal(TenantMemberStatus.Active, back.Status);
        Assert.Equal((short)1, (await db.TenantMembers.FirstAsync(x => x.UserId == uid)).Status);
    }

    [Fact]
    [Trait("Fn", "M00.F02.I07")]
    public async Task RolesPut_replacesRoles_withinTenantScope()
    {
        var (db, ctrl) = Make("members-put-roles");
        var u = MakeUser("carol");
        db.SysUsers.Add(u);
        var m = MakeMember(u.Id);
        var r1 = new DbRole
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ClientId = "lab-management",
            RoleCode = "r1", RoleName = "r1", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var r2 = new DbRole
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ClientId = "lab-management",
            RoleCode = "r2", RoleName = "r2", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.SysRoles.AddRange(r1, r2);
        db.TenantMembers.Add(m);
        await db.SaveChangesAsync();
        m.Roles.Add(r1);
        await db.SaveChangesAsync();

        // r1 → r2 + 未知 id（静默忽略）
        var dto = await ctrl.Roles(TenantId.ToString(), u.Id.ToString(),
            new SetTenantMemberRolesRequest { RoleIds = new List<string> { r2.Id.ToString(), Guid.NewGuid().ToString() } });
        Assert.Equal(r2.Id.ToString(), Assert.Single(dto.RoleIds));
        var member = await db.TenantMembers.Include(x => x.Roles).FirstAsync(x => x.UserId == u.Id);
        Assert.Equal(r2.Id, Assert.Single(member.Roles).Id);
    }
}
