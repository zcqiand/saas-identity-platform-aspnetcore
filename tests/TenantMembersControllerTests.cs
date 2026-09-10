using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using PersistDb = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbUser = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysUser;
using DbMember = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantMember;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// M00.F02.I06/I01/I08 — invitation 建 status=2 invited 真 user 行（I42 方案 C）+
/// status 枚举显式映射（DB 1=active/2=invited/0=disabled ↔ 生成枚举 Active=0/Invited=1/Disabled=2）。
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

    [Fact]
    [Trait("Fn", "M00.F02.I06")]
    public async Task Invitations_createsInvitedUserAndActiveMember()
    {
        var (db, ctrl) = Make("invite-1");
        var email = "newbie@example.com";
        var dto = await ctrl.Invitations(TenantId.ToString(), new Body3 { Email = email });

        // I42 oracle：user.status="invited"，member.status="active"，memberName=username
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
    public async Task MembersGet_mapsDbStatusToEnum_notRawCast()
    {
        var (db, ctrl) = Make("members-map");
        db.SysUsers.Add(new DbUser
        {
            Id = Guid.NewGuid(), Username = "u1", Email = "u1@x.com", Password = "p",
            Status = 2, // DB invited
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var u = await db.SysUsers.FirstAsync();
        db.TenantMembers.Add(new DbMember
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = u.Id, MemberName = "u1",
            Status = 1, // DB active
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var page = await ctrl.MembersGet(TenantId.ToString(), 0, 20, null);
        var item = Assert.Single(page.Items);
        // 旧裸 cast 会把 DB 2 错映射成 Disabled —— 显式映射后必须是 Invited
        Assert.Equal(SysUserStatus.Invited, item.User.Status);
        Assert.Equal(TenantMemberStatus.Active, item.Member.Status);
    }

    [Fact]
    [Trait("Fn", "M00.F02.I01")]
    public async Task MembersGet_statusFilter_usesDbValueNotEnumNumber()
    {
        var (db, ctrl) = Make("members-filter");
        db.SysUsers.Add(new DbUser
        {
            Id = Guid.NewGuid(), Username = "u1", Email = "u1@x.com", Password = "p",
            Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var u = await db.SysUsers.FirstAsync();
        db.TenantMembers.Add(new DbMember
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = u.Id, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
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
        db.SysUsers.Add(new DbUser
        {
            Id = uid, Username = "u1", Email = "u1@x.com", Password = "p",
            Status = 1, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        db.TenantMembers.Add(new DbMember
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = uid, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await ctrl.Status(TenantId.ToString(), uid.ToString(), new Body4 { Status = TenantMemberStatus.Disabled });

        var m = await db.TenantMembers.FirstAsync(x => x.UserId == uid);
        Assert.Equal((short)0, m.Status); // DB disabled=0，不是枚举编号 1
    }
}
