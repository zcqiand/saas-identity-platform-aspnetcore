using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Xunit;
using UserEntity = Saas.Identity.AspNetCore.Domain.Entities.User;
using TenantEntity = Saas.Identity.AspNetCore.Domain.Entities.Tenant;
using MembershipEntity = Saas.Identity.AspNetCore.Domain.Entities.TenantMembership;
using GrantEntity = Saas.Identity.AspNetCore.Domain.Entities.RoleMenuGrant;

namespace Saas.Identity.AspNetCore.Tests.Harness;

/// <summary>
/// M09.F03 my-menus 真库切片测试：把 MeController.Menus 的查询链
/// （membership.roleIds uuid[] 读取 → RoleMenuGrants Contains → 菜单树父链补全 → apps JOIN）
/// 放到 saas_test 真 PG 上跑 —— PG native enum / uuid[] / jsonb 走真实驱动映射。
/// InMemory 版（MeControllerTests）保留：快速路径覆盖；本测试锁 SQL 方言行为。
///
/// 数据策略：apps/menus 复用 saas_test 既有种子（apps_code_unique 全局唯一，不再自种）；
/// tenant/user/membership/grant 是本测试自种行（Guid 随机），dispose 按 Guid 清理。
/// 硬依赖共享 PG，连不上即败（TestDb.RequireReachable）。
/// 分层（CI=翻译性 / gate=真库）：[Trait("Category", "RealDb")]，ci.yml --filter 排除。
/// </summary>
[Trait("Category", "RealDb")]
public sealed class MeMenusPgTest : IDisposable
{
    private static readonly Guid LabAppId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly AppDbContext db = TestDb.CreateContext();
    private readonly Guid tenantId = Guid.NewGuid();
    private readonly Guid uid = Guid.NewGuid();

    public MeMenusPgTest()
    {
        TestDb.RequireReachable();
        Seed();
    }

    public void Dispose()
    {
        db.RoleMenuGrants.Where(g => g.TenantId == tenantId).ExecuteDelete();
        db.Roles.Where(r => r.TenantId == tenantId).ExecuteDelete();
        db.TenantMemberships.Where(m => m.TenantId == tenantId).ExecuteDelete();
        db.Users.Where(u => u.Id == uid).ExecuteDelete();
        db.Tenants.Where(t => t.Id == tenantId).ExecuteDelete();
        db.SaveChanges();
    }

    private void Seed()
    {
        // 真库 FK 链：tenants → users/roles → role_menu_grants/tenant_memberships。
        // EF 批量插入不保依赖序，分级 SaveChanges（InMemory 版无此约束 —— 这正是
        // 真库测试的价值：FK 拓扑即文档）。
        db.Tenants.Add(new TenantEntity
        {
            Id = tenantId, Code = $"pg-t-{tenantId:N}", Name = "PG 测试租户",
            Status = "active", Settings = new Dictionary<string, object?>(),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();

        db.Users.Add(new UserEntity
        {
            Id = uid, TenantId = tenantId,
            Username = $"u-{uid:N}", Email = $"{uid:N}@pg-test.local",
            PasswordHash = "plain:x", Status = "active",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            DisplayName = "PG Tester", RoleIds = new List<Guid>(),
        });

        // 授权到种子菜单 m-lab-dash（apps/menus 复用 saas_test 种子行）
        var dashId = db.Menus.Where(m => m.AppId == LabAppId && m.Code == "m-lab-dash")
            .Select(m => m.Id).First();
        var roleId = Guid.NewGuid();
        // rmg_role_fk：role_menu_grants.role_id → roles.id。真库 FK 下 EF 批量插入不保序，
        // 依赖关系须分批 SaveChanges（tenant → role → grant/membership）
        db.Roles.Add(new Role
        {
            Id = roleId, TenantId = tenantId, Code = $"r-{roleId:N}"[..20], Name = "PG 测试角色",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        db.RoleMenuGrants.Add(new GrantEntity
        {
            RoleId = roleId, TenantId = tenantId,
            MenuIds = new List<Guid> { dashId },
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.TenantMemberships.Add(new MembershipEntity
        {
            Id = Guid.NewGuid(), UserId = uid, TenantId = tenantId,
            RoleIds = new List<Guid> { roleId }, Status = "active",
            JoinedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    [Fact]
    [Trait("Fn", "M09.F03.I02")]
    public async Task Menus_membershipRoleGrants_resolvesAgainstRealPg()
    {
        var http = new DefaultHttpContext();
        http.Items[SaasSessionMiddleware.ItemsKey] =
            new SaasSession(uid, tenantId, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        var controller = new MeController(db, new HttpContextAccessor { HttpContext = http });

        var menus = await controller.Menus();

        // 真 PG：membership.roleIds uuid[] → grants Contains → menus 树装配全链
        Assert.True(menus.ContainsKey("lab-management"));
        Assert.NotEmpty(menus["lab-management"]);
    }
}
