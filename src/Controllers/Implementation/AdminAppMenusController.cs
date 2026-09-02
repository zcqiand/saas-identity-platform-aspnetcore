using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using DbMenu = Saas.Identity.AspNetCore.Domain.Entities.Menu;
// alias 避免与 NSwag-generated DTO `Menu` 冲突
using ApiMenu = Saas.Identity.AspNetCore.Controllers.Generated.Menu;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M08 菜单 CRUD（应用下，平台 admin 操作）。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// </summary>
public class AdminAppMenusController : AdminAppMenusControllerBase
{
    private readonly AppDbContext _db;

    public AdminAppMenusController(AppDbContext db) { _db = db; }

    // === DTO ↔ Entity 转换 ===

    private static ApiMenu ToDto(DbMenu e) => new()
    {
        Id = e.Id,
        AppId = e.AppId,
        ParentId = e.ParentId ?? Guid.Empty,
        Code = e.Code,
        Name = e.Name,
        Path = e.Path,
        Icon = e.Icon,
        Type = ToDtoType(e.Type),
        SortOrder = e.SortOrder,
        Status = ToDtoStatus(e.Status),
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    private static MenuType ToDtoType(MenuTypePg s) => s switch
    {
        MenuTypePg.@group => MenuType.Group,
        MenuTypePg.page => MenuType.Page,
        _ => MenuType.Action,
    };

    private static MenuTypePg ToDbType(MenuType t) => t switch
    {
        MenuType.Group => MenuTypePg.@group,
        MenuType.Page => MenuTypePg.page,
        _ => MenuTypePg.action,
    };

    private static MenuStatus ToDtoStatus(MenuStatusPg s) => s switch
    {
        MenuStatusPg.active => MenuStatus.Active,
        _ => MenuStatus.Disabled,
    };

    private static MenuStatusPg ToDbStatus(MenuStatus s) => s switch
    {
        MenuStatus.Active => MenuStatusPg.active,
        _ => MenuStatusPg.disabled,
    };

    // === endpoints ===

    public override async Task<ICollection<ApiMenu>> MenusGet(string appId)
    {
        var aid = await ResolveAppIdAsync(appId);
        var rows = await _db.Menus.Where(m => m.AppId == aid).ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    public override async Task<ApiMenu> MenusPost(string appId, CreateMenuRequest body)
    {
        var aid = await ResolveAppIdAsync(appId);
        var e = new DbMenu
        {
            Id = Guid.NewGuid(),
            AppId = aid,
            // 2026-09-01 contract-test I51：NSwag 生成的 ParentId 是 non-nullable Guid，
            // 未传时为 Guid.Empty → 插 parent_id 撞 menus_parent_fk（23503）。Empty → null（顶级）。
            ParentId = body.ParentId == Guid.Empty ? null : body.ParentId,
            Code = body.Code,
            Name = body.Name,
            Path = body.Path,
            Icon = body.Icon,
            Type = ToDbType(body.Type),
            SortOrder = body.SortOrder,
            Status = ToDbStatus(body.Status),
            // 2026-09-01 contract-test M96.F02.I51：显式写时间戳——实体 DateTimeOffset
            // 默认 0001-01-01 超 timestamptz 范围，EF save 抛 INTERNAL_ERROR 500
            // （同 AdminAppsController.AppsPost / AdminTenantsController.TenantsPost 修法）
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Menus.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ApiMenu> MenusGet(string appId, string menuId)
    {
        var id = Guid.Parse(menuId);
        // 2026-09-01 contract-test I52：不存在 id → 404（FirstAsync 抛 → 500）
        var e = await _db.Menus.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Menu {menuId} not found");
        return ToDto(e);
    }

    public override async Task<ApiMenu> MenusPatch(string appId, string menuId, UpdateMenuRequest body)
    {
        var id = Guid.Parse(menuId);
        var e = await _db.Menus.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Menu {menuId} not found");
        if (body.Name != null) e.Name = body.Name;
        if (body.Path != null) e.Path = body.Path;
        if (body.Icon != null) e.Icon = body.Icon;
        if (body.ParentId != null && Guid.TryParse(body.ParentId, out var pid)) e.ParentId = pid;
        e.Type = ToDbType(body.Type);
        e.SortOrder = body.SortOrder;
        e.Status = ToDbStatus(body.Status);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task MenusDelete(string appId, string menuId)
    {
        var id = Guid.Parse(menuId);
        var e = await _db.Menus.FirstOrDefaultAsync(m => m.Id == id);
        if (e != null)
        {
            _db.Menus.Remove(e);
            await _db.SaveChangesAsync();
        }
    }

    public override async Task<ApiMenu> Parent(string appId, string menuId, Body body)
    {
        var id = Guid.Parse(menuId);
        var e = await _db.Menus.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Menu {menuId} not found");
        e.ParentId = string.IsNullOrEmpty(body.ParentId) ? null : Guid.Parse(body.ParentId);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ICollection<ApiMenu>> Reorder(string appId, string menuId, ReorderMenuRequest body)
    {
        var aid = await ResolveAppIdAsync(appId);
        // 2026-09-01 contract-test I55：按数组下标写 sortOrder（对齐 nextjs reorder route），
        // 此前是 no-op stub（只回列表不写序）。
        for (var i = 0; i < body.OrderedMenuIds.Count; i++)
        {
            var mid = Guid.Parse(body.OrderedMenuIds[i]);
            var row = await _db.Menus.FirstOrDefaultAsync(m => m.Id == mid && m.AppId == aid);
            if (row != null)
            {
                row.SortOrder = i;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        await _db.SaveChangesAsync();
        var rows = await _db.Menus
            .Where(m => m.AppId == aid)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Code)
            .ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    // OpenAPI 声明 appId 是 string（不约束 Guid 格式）。
    // 前端路由用 App.Code（slug 如 "lab-management"），不是 App.Id（Guid）。
    // v0.4.0 之前 InMemoryStore 按 Code 索引直通了，迁 EF Core 后变 Guid.Parse(appId) 撞回归。
    // 兼容两路：先当 Guid 试，存在就返；否则按 Code 查。
    private async Task<Guid> ResolveAppIdAsync(string appIdOrCode)
    {
        if (Guid.TryParse(appIdOrCode, out var gid))
        {
            var existsById = await _db.Apps.AnyAsync(a => a.Id == gid);
            if (existsById) return gid;
        }
        var byCode = await _db.Apps.FirstOrDefaultAsync(a => a.Code == appIdOrCode)
            ?? throw new KeyNotFoundException($"app '{appIdOrCode}' not found");
        return byCode.Id;
    }
}