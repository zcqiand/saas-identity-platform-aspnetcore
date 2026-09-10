using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbRole = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysRole;
using DbMenu = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysMenu;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// M00.F04.I02-I04 — role-menus 聚合返回（I20 方案 C，2026-09-10）。
/// GET/PUT 返回 RoleMenuGrant {roleId, tenantId, menuIds[], updatedAt}（对齐 msw/nextjs oracle）。
/// guard-first；EF InMemory。
/// </summary>
public class TenantRoleMenusControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (AppDbContext db, TenantRoleMenusController ctrl) Make(string name, Guid roleId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name).Options;
        var db = new AppDbContext(options);
        db.SysRoles.Add(new DbRole
        {
            Id = roleId, TenantId = TenantId, ClientId = "lab-management",
            RoleCode = "admin", RoleName = "Admin", Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        var guard = new TenantGuard(new StubTenantContext { TenantId = TenantId.ToString() });
        var ctrl = new TenantRoleMenusController(guard, db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (db, ctrl);
    }

    private static DbMenu Menu(int i) => new()
    {
        Id = Guid.NewGuid(), ClientId = "lab-management", ParentId = Guid.Empty,
        Title = $"menu-{i}", Type = 1, SortOrder = i, Status = 1, CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    [Trait("Fn", "M00.F04.I03")]
    public async Task MenusPut_returnsGrantAggregate()
    {
        var roleId = Guid.NewGuid();
        var (db, ctrl) = Make("role-menus-put", roleId);
        var m1 = Menu(1);
        var m2 = Menu(2);
        db.SysMenus.AddRange(m1, m2);
        await db.SaveChangesAsync();

        var before = DateTime.UtcNow.AddSeconds(-5);
        var grant = await ctrl.MenusPut(TenantId.ToString(), roleId.ToString(), "lab-management",
            new SetSysRoleMenusRequest { MenuIds = new List<string> { m1.Id.ToString(), m2.Id.ToString() } });

        Assert.Equal(roleId, grant.RoleId);
        Assert.Equal(TenantId, grant.TenantId);
        Assert.Equal(2, grant.MenuIds.Count);
        Assert.Contains(m1.Id.ToString(), grant.MenuIds);
        Assert.Contains(m2.Id.ToString(), grant.MenuIds);
        Assert.True(grant.UpdatedAt.UtcDateTime >= before, "updatedAt must be touched on PUT");
    }

    [Fact]
    [Trait("Fn", "M00.F04.I02")]
    public async Task MenusGet_returnsGrantAggregate()
    {
        var roleId = Guid.NewGuid();
        var (db, ctrl) = Make("role-menus-get", roleId);
        var m = Menu(1);
        db.SysMenus.Add(m);
        var role = await db.SysRoles.FirstAsync(r => r.Id == roleId);
        role.Menus.Add(m);
        await db.SaveChangesAsync();

        var grant = await ctrl.MenusGet(TenantId.ToString(), roleId.ToString(), "lab-management");

        Assert.Equal(roleId, grant.RoleId);
        Assert.Equal(TenantId, grant.TenantId);
        Assert.Single(grant.MenuIds, m.Id.ToString());
        Assert.True((DateTime.UtcNow - grant.UpdatedAt.UtcDateTime).TotalMinutes < 10);
    }

    [Fact]
    [Trait("Fn", "M00.F04.I03")]
    public async Task MenusPut_replacesAll()
    {
        var roleId = Guid.NewGuid();
        var (db, ctrl) = Make("role-menus-replace", roleId);
        var m1 = Menu(1);
        var m2 = Menu(2);
        db.SysMenus.AddRange(m1, m2);
        var role = await db.SysRoles.FirstAsync(r => r.Id == roleId);
        role.Menus.Add(m1);
        await db.SaveChangesAsync();

        var grant = await ctrl.MenusPut(TenantId.ToString(), roleId.ToString(), "lab-management",
            new SetSysRoleMenusRequest { MenuIds = new List<string> { m2.Id.ToString() } });

        Assert.Single(grant.MenuIds, m2.Id.ToString());
        Assert.DoesNotContain(m1.Id.ToString(), grant.MenuIds);
    }

    [Fact]
    [Trait("Fn", "M00.F04.I03")]
    public async Task MenusPut_unknownRole_throws404()
    {
        var (_, ctrl) = Make("role-menus-404", Guid.NewGuid());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ctrl.MenusPut(
            TenantId.ToString(), Guid.NewGuid().ToString(), "lab-management",
            new SetSysRoleMenusRequest { MenuIds = new List<string>() }));
    }

    [Fact]
    [Trait("Fn", "M00.F01.I03")]
    public async Task RoleMenus_ops_guardRejectsForeignTenant()
    {
        var (_, ctrl) = Make("role-menus-guard", Guid.NewGuid());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctrl.MenusGet(
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "lab-management"));
    }
}
