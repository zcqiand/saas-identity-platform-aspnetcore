using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Xunit;
using AppEntity = Saas.Identity.AspNetCore.Domain.Entities.App;
using UserEntity = Saas.Identity.AspNetCore.Domain.Entities.User;
using MenuEntity = Saas.Identity.AspNetCore.Domain.Entities.Menu;
using RoleMenuGrantEntity = Saas.Identity.AspNetCore.Domain.Entities.RoleMenuGrant;
using ApiKeyEntity = Saas.Identity.AspNetCore.Domain.Entities.ApiKey;
using AuditEventEntity = Saas.Identity.AspNetCore.Domain.Entities.AuditEvent;
using AuditRetentionPolicyEntity = Saas.Identity.AspNetCore.Domain.Entities.AuditRetentionPolicy;
using PermissionEntity = Saas.Identity.AspNetCore.Domain.Entities.Permission;
using RoleEntity = Saas.Identity.AspNetCore.Domain.Entities.Role;
using RolePermissionEntity = Saas.Identity.AspNetCore.Domain.Entities.RolePermission;
using TenantEntity = Saas.Identity.AspNetCore.Domain.Entities.Tenant;
using TenantMembershipEntity = Saas.Identity.AspNetCore.Domain.Entities.TenantMembership;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M00.F02 当前用户身份 (whoami + 跨租户切换 + 我的菜单)。
/// M09.F03 my-menus 真实现 (PLAN-2026-002 / REQ-2026-021) 接 role_menu_grants JOIN,
/// 测试需要 TenantMembership + RoleMenuGrant 留在 model 里。其它 entity (ApiKey/Audit/Permission/Role/RolePermission/Tenant) 仍是 Ignore。
/// InMemory provider 不能跑 PG native enum / uuid[] 转换 — MemberStatus 等用 string 替代; List&lt;Guid&gt; 直接走 .NET。
/// </summary>
internal class MeTestDbContext : AppDbContext
{
    public MeTestDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Ignore<ApiKeyEntity>();
        modelBuilder.Ignore<AuditEventEntity>();
        modelBuilder.Ignore<AuditRetentionPolicyEntity>();
        modelBuilder.Ignore<PermissionEntity>();
        modelBuilder.Ignore<RoleEntity>();
        modelBuilder.Ignore<RolePermissionEntity>();
        modelBuilder.Ignore<TenantEntity>();
    }
}

/// <summary>
/// M00.F02 当前用户身份（whoami + 跨租户切换 + 我的菜单）。
/// </summary>
public class MeControllerTests
{
    private static MeController BuildMeController(DefaultHttpContext? ctx = null, Guid? userId = null)
    {
        var (c, _) = BuildMeControllerWithGrants(ctx, userId);  // default seedMembership+seedGrant=true
        return c;
    }

    /// <summary>
    /// Build controller for M09.F03.I02-I04 真逻辑 tests (PLAN-2026-002)。
    /// seedMembership=false: 不写 membership — I02 空 roleIds 路径。
    /// seedGrant=false:     不写 grant — I02 roleIds 有但 grants=[] 路径。
    /// seedMembership=true + seedGrant=true: default happy path (1 role grant dashboard)。
    /// grantMenuIds (default=[dashboard]): grant 授权的 menuIds, 让 I03 父链补全测试可以 grant child 而非 dashboard。
    /// extraMenus: 附加 menus (e.g. I03 父链补全测试)。
    /// </summary>
    private static (MeController controller, Guid uid) BuildMeControllerWithGrants(
        DefaultHttpContext? ctx = null,
        Guid? userId = null,
        bool seedMembership = true,
        bool seedGrant = true,
        List<Guid>? grantMenuIds = null,
        List<MenuEntity>? extraMenus = null)
    {
        var dbOpts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"me-test-{Guid.NewGuid()}")
            .Options;
        var db = new MeTestDbContext(dbOpts);
        var uid = userId ?? Guid.NewGuid();
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        db.Apps.Add(new AppEntity
        {
            Id = appId, Code = "lab-management", Name = "lab-mgmt", Status = "active",
            ClientId = appId.ToString(),
            RedirectUris = new List<string> { "https://lab-vue.xiangru.uk/login" },
            Scopes = new List<string> { "lab.read" },
            GrantTypes = new List<string> { "authorization_code" },
            IsFirstParty = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(new UserEntity
        {
            Id = uid, TenantId = Guid.NewGuid(), Username = "alice", Email = "alice@acme.io",
            PasswordHash = "plain:x", Status = "active", CreatedAt = DateTimeOffset.UtcNow,
            DisplayName = "Alice",
        });
        var dashboardId = Guid.NewGuid();
        db.Menus.Add(new MenuEntity
        {
            Id = dashboardId, AppId = appId, Code = "m-dashboard", Name = "工作台",
            Path = "/", Type = "page", Status = "active",
            SortOrder = 1, CreatedAt = DateTimeOffset.UtcNow,
        });
        if (extraMenus != null)
        {
            foreach (var m in extraMenus) db.Menus.Add(m);
        }
        Guid? roleId = null;
        if (seedGrant)
        {
            roleId = Guid.NewGuid();
            db.RoleMenuGrants.Add(new RoleMenuGrantEntity
            {
                RoleId = roleId.Value, TenantId = Guid.NewGuid(),
                MenuIds = grantMenuIds ?? new List<Guid> { dashboardId },
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        if (seedMembership)
        {
            var roleIds = roleId.HasValue
                ? new List<Guid> { roleId.Value }
                : new List<Guid> { Guid.NewGuid() };  // 不关联 grant, 测 roleIds→空 grants
            db.TenantMemberships.Add(new TenantMembershipEntity
            {
                Id = Guid.NewGuid(), UserId = uid, TenantId = Guid.NewGuid(),
                RoleIds = roleIds, Status = "active",
                JoinedAt = DateTimeOffset.UtcNow,
            });
        }
        db.SaveChanges();

        var controller = new MeController(db, new HttpContextAccessor { HttpContext = ctx ?? new DefaultHttpContext() });
        return (controller, uid);
    }

    // === M09.F03.I01 me/menus session 校验 ===

    [Fact]
    [Trait("Fn", "M09.F03.I01")]
    public async Task Menus_noSession_throwsUnauthorized()
    {
        var c = BuildMeController(new DefaultHttpContext());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Menus());
    }

    [Fact]
    [Trait("Fn", "M09.F03.I01")]
    public async Task Menus_withValidSession_returnsDict()
    {
        var ctx = new DefaultHttpContext();
        var (c, uid) = BuildMeControllerWithGrants(ctx: ctx);  // default happy path
        var session = new SaasSession(uid, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        ctx.Items[SaasSessionMiddleware.ItemsKey] = session;
        var menus = await c.Menus();
        Assert.NotEmpty(menus);
        Assert.True(menus.ContainsKey("lab-management"));
        Assert.NotEmpty(menus["lab-management"]);
    }

    // === M09.F03.I02 — 角色授权菜单 ID 查询 (空路径) ===

    [Fact]
    [Trait("Fn", "M09.F03.I02")]
    public async Task Menus_userWithoutMembership_returnsEmpty()
    {
        // 没有 membership → roleIds=[] → 返回空 Map (early exit)
        var ctx = new DefaultHttpContext();
        var (c, uid) = BuildMeControllerWithGrants(
            ctx: ctx,
            seedMembership: false,
            seedGrant: false);
        var session = new SaasSession(uid, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        ctx.Items[SaasSessionMiddleware.ItemsKey] = session;
        var menus = await c.Menus();
        Assert.Empty(menus);
    }

    [Fact]
    [Trait("Fn", "M09.F03.I02")]
    public async Task Menus_membershipButRoleHasNoGrants_returnsEmpty()
    {
        // membership 有 roleId, 但 role_menu_grants 表里没那个 role 行 → grantedMenuIds=[]
        var ctx = new DefaultHttpContext();
        var (c, uid) = BuildMeControllerWithGrants(
            ctx: ctx,
            seedMembership: true,
            seedGrant: false);
        var session = new SaasSession(uid, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        ctx.Items[SaasSessionMiddleware.ItemsKey] = session;
        var menus = await c.Menus();
        Assert.Empty(menus);
    }

    // === M09.F03.I03 — 菜单树装配 + 父链补全 ===

    [Fact]
    [Trait("Fn", "M09.F03.I03")]
    public async Task Menus_grantedChildIncludesParentChain()
    {
        // user 只授权 child; parent "group" 不在 grant 但应该自动补上
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var ctx = new DefaultHttpContext();
        // 必须先塞 parent + child 进 menus; grant 指 child; helper 默认 grant 是 dashboard, 不适用
        // 这里手动控: 不通过 helper 默认 seedGrant, 改成只 seed child 命名 grant
        var (c, uid) = BuildMeControllerWithGrants(
            ctx: ctx,
            seedGrant: true,
            grantMenuIds: new List<Guid> { childId },
            extraMenus: new List<MenuEntity>
            {
                new MenuEntity
                {
                    Id = parentId, AppId = appId, ParentId = null, Code = "m-group", Name = "分组",
                    Type = "group", SortOrder = 0, Status = "active",
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                new MenuEntity
                {
                    Id = childId, AppId = appId, ParentId = parentId, Code = "m-leaf", Name = "叶子",
                    Type = "page", SortOrder = 1, Status = "active",
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            });
        var session = new SaasSession(uid, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        ctx.Items[SaasSessionMiddleware.ItemsKey] = session;
        var menus = await c.Menus();
        Assert.True(menus.ContainsKey("lab-management"));
        var roots = menus["lab-management"];
        // roots 应只有 parent; child 是叶子 (ParentId=parentId) 不应是 root
        Assert.Single(roots);
        var onlyRoot = roots.First();
        Assert.Equal(parentId, onlyRoot.Id);
        Assert.Equal("Group", onlyRoot.Type.ToString());
        // 父链补全: parent.children 包括 child
        Assert.Single(onlyRoot.Children);
        Assert.Equal(childId, onlyRoot.Children.First().Id);
    }

    // === T-11 (PLAN-2026-001) 集成覆盖：login -> cookie -> middleware -> me/menus ===

    [Fact]
    [Trait("Fn", "M09.F03.I01")]
    [Trait("Fn", "M03.F01.I01")]
    public async Task Flow_meMenus_withRealLoginSession()
    {
        // 真 AuthController.Login 写 cookie -> middleware 注入 -> MeController.Menus 返回菜单
        var (db, uid, _) = OauthControllerTests.OauthSessionFlow.BuildFlowDb();
        var (auth, loginCtx, sessions) = OauthControllerTests.OauthSessionFlow.LoginOnce(db);
        await auth.Login(new()
        {
            Username = OauthControllerTests.OauthSessionFlow.FlowUsername,
            Password = OauthControllerTests.OauthSessionFlow.FlowPassword,
        });
        var sid = OauthControllerTests.OauthSessionFlow.ExtractSid(loginCtx);
        Assert.NotNull(sid);

        var ctx = await OauthControllerTests.OauthSessionFlow.InvokeMiddleware(sessions, sid);
        var session = ctx.Items[SaasSessionMiddleware.ItemsKey] as SaasSession;
        Assert.NotNull(session);
        Assert.Equal(uid, session!.UserId);

        var me = new MeController(db, new HttpContextAccessor { HttpContext = ctx });
        var menus = await me.Menus();
        Assert.NotEmpty(menus);
        // 流程库 seed 的 menu 挂在 lab-management app 下
        Assert.True(menus.ContainsKey("lab-management"));
        Assert.NotEmpty(menus["lab-management"]);
    }

    [Fact]
    [Trait("Fn", "M09.F03.I01")]
    public async Task Flow_meMenus_expiredLoginSession_throwsUnauthorized()
    {
        // 登录后 session 过期 -> middleware 不注入 -> Menus 401
        var (db, _, _) = OauthControllerTests.OauthSessionFlow.BuildFlowDb();
        var (auth, loginCtx, sessions) = OauthControllerTests.OauthSessionFlow.LoginOnce(db,
            sessions: new SaasSessionStore(TimeSpan.FromMilliseconds(100)));
        await auth.Login(new()
        {
            Username = OauthControllerTests.OauthSessionFlow.FlowUsername,
            Password = OauthControllerTests.OauthSessionFlow.FlowPassword,
        });
        await Task.Delay(200);

        var ctx = await OauthControllerTests.OauthSessionFlow.InvokeMiddleware(sessions, OauthControllerTests.OauthSessionFlow.ExtractSid(loginCtx));
        Assert.False(ctx.Items.ContainsKey(SaasSessionMiddleware.ItemsKey));
        var me = new MeController(db, new HttpContextAccessor { HttpContext = ctx });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => me.Menus());
    }

    // === M00.F02 占位 Phase 5 ===

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M00.F02.I01")]
    public Task Whoami_returnsCurrentUser() => Task.CompletedTask;

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M00.F02.I02")]
    public Task Tenants_returnsMemberships() => Task.CompletedTask;

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M00.F02.I03")]
    public Task Switch_returnsNewToken() => Task.CompletedTask;
}