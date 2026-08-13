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

    private static MenuType ToDtoType(string s) => s switch
    {
        "group" => MenuType.Group,
        "page" => MenuType.Page,
        _ => MenuType.Action,
    };

    private static string ToDbType(MenuType t) => t switch
    {
        MenuType.Group => "group",
        MenuType.Page => "page",
        _ => "action",
    };

    private static MenuStatus ToDtoStatus(string s) => s switch
    {
        "active" => MenuStatus.Active,
        _ => MenuStatus.Disabled,
    };

    private static string ToDbStatus(MenuStatus s) => s switch
    {
        MenuStatus.Active => "active",
        _ => "disabled",
    };

    // === endpoints ===

    public override async Task<ICollection<ApiMenu>> MenusGet(string appId)
    {
        var aid = Guid.Parse(appId);
        var rows = await _db.Menus.Where(m => m.AppId == aid).ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    public override async Task<ApiMenu> MenusPost(string appId, CreateMenuRequest body)
    {
        var e = new DbMenu
        {
            Id = Guid.NewGuid(),
            AppId = Guid.Parse(appId),
            ParentId = body.ParentId,
            Code = body.Code,
            Name = body.Name,
            Path = body.Path,
            Icon = body.Icon,
            Type = ToDbType(body.Type),
            SortOrder = body.SortOrder,
            Status = ToDbStatus(body.Status),
        };
        _db.Menus.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ApiMenu> MenusGet(string appId, string menuId)
    {
        var id = Guid.Parse(menuId);
        var e = await _db.Menus.FirstAsync(m => m.Id == id);
        return ToDto(e);
    }

    public override async Task<ApiMenu> MenusPatch(string appId, string menuId, UpdateMenuRequest body)
    {
        var id = Guid.Parse(menuId);
        var e = await _db.Menus.FirstAsync(m => m.Id == id);
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
        var e = await _db.Menus.FirstAsync(m => m.Id == id);
        e.ParentId = string.IsNullOrEmpty(body.ParentId) ? null : Guid.Parse(body.ParentId);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ICollection<ApiMenu>> Reorder(string appId, string menuId, ReorderMenuRequest body)
    {
        var aid = Guid.Parse(appId);
        var rows = await _db.Menus.Where(m => m.AppId == aid).ToListAsync();
        return rows.Select(ToDto).ToList();
    }
}