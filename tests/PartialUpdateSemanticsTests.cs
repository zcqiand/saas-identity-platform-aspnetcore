using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbSysMenu = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysMenu;
using DbTenant = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.Tenant;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// 5.26 — 邻位 partial-update 语义（5.18 先例 333d8ac 同款：「不传 = 保持原值」）。
/// 修前既有行为：MenusPatch 不带 type/status/sortOrder/parentId 重置默认值、
/// TenantsPatch 不带 status 覆写回 Active。两步断言：先 PATCH 只改一个无关注段 →
/// 回读断言目标字段不变。
/// </summary>
public class PartialUpdateSemanticsTests
{
    private static AppDbContext MakeDb(string name)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name).Options);

    [Fact]
    [Trait("Fn", "M04.F04.I04")]
    public async Task MenusPatch_withoutOptionalFields_keepsOriginalValues()
    {
        var db = MakeDb("partial-menu-patch");
        var parentId = Guid.NewGuid();
        db.SysMenus.Add(new DbSysMenu
        {
            Id = Guid.NewGuid(), ClientId = "test-client", ParentId = parentId,
            Title = "old-title", Type = 1, Path = "/old", Icon = "old-icon",
            SortOrder = 7, Status = 1, CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        var ctrl = new ClientMenusController(db);
        var menuId = db.SysMenus.Single(m => m.ClientId == "test-client").Id.ToString();

        // 只改 title（无关注段），不带 type/status/sortOrder/parentId
        var dto = await ctrl.MenusPatch("test-client", menuId, new UpdateSysMenuRequest
        {
            Title = "new-title",
        });

        Assert.Equal("new-title", dto.Title);
        // 目标字段：不传不改（修前被重置 Type=Directory/Status=0/SortOrder=0/ParentId=Empty）
        Assert.Equal(SysMenuType.Menu, dto.Type);
        Assert.Equal(1, dto.Status);
        Assert.Equal(7, dto.SortOrder);
        Assert.Equal(parentId, dto.ParentId);
    }

    [Fact]
    [Trait("Fn", "M00.F01.I04")]
    public async Task TenantsPatch_withoutStatus_keepsOriginalStatus()
    {
        var db = MakeDb("partial-tenant-patch");
        db.Tenants.Add(new DbTenant
        {
            Id = Guid.NewGuid(), TenantKey = "t-partial", Name = "old-name",
            Status = 2, // suspended
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        var ctrl = new AdminTenantsController(db);
        var tenantId = db.Tenants.Single(t => t.TenantKey == "t-partial").Id.ToString();

        // 只改 name，不带 status
        var dto = await ctrl.TenantsPatch(tenantId, new UpdateTenantRequest
        {
            Name = "new-name",
        });

        Assert.Equal("new-name", dto.Name);
        // 目标字段：不传不改（修前被覆写回 Active）
        Assert.Equal(TenantStatus.Suspended, dto.Status);
    }
}
