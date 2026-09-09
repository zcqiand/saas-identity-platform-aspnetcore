using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbSysMenu = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysMenu;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
// alias to disambiguate DTO SysMenu (NSwag) from entity DbSysMenu (scaffold)
using ApiSysMenu = Saas.Identity.AspNetCore.Controllers.Generated.SysMenu;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// v0.5.0 NSwag 重 emit 后：admin-app-menus 拆为 ClientMenus（M04 菜单落 clientId 维度），
/// 路由前缀从 /admin/oauth-clients/{appId}/sys-menus → /clients/{clientId}/menus。
/// DTO：Menu → SysMenu；MenuType → SysMenuType；MenuStatus enum 取消，status 改 int。
/// CreatedAt 是 DateTimeOffset（NSwag emit），不再是 DateTime。
/// </summary>
public class ClientMenusController : ClientMenusControllerBase
{
    private readonly AppDbContext _db;

    public ClientMenusController(AppDbContext db) { _db = db; }

    private static ApiSysMenu ToDto(DbSysMenu e) => new()
    {
        Id = e.Id,
        ClientId = e.ClientId,
        ParentId = e.ParentId,
        Title = e.Title,
        Type = (SysMenuType)(int)e.Type,
        Path = e.Path,
        Icon = e.Icon,
        SortOrder = e.SortOrder,
        Status = (int)e.Status,
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc)),
    };

    public override async Task<ICollection<ApiSysMenu>> MenusGet(string clientId)
    {
        var rows = await _db.SysMenus.Where(m => m.ClientId == clientId).ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    public override async Task<ApiSysMenu> MenusPost(string clientId, CreateSysMenuRequest body)
    {
        var e = new DbSysMenu
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            ParentId = body.ParentId,
            Title = body.Title,
            Type = (short)body.Type,
            Path = body.Path,
            Icon = body.Icon,
            SortOrder = body.SortOrder,
            CreatedAt = DateTime.UtcNow,
        };
        _db.SysMenus.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ApiSysMenu> MenusGet(string clientId, string menuId)
    {
        var id = Guid.Parse(menuId);
        var e = await _db.SysMenus.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Menu {menuId} not found");
        return ToDto(e);
    }

    public override async Task<ApiSysMenu> MenusPatch(string clientId, string menuId, UpdateSysMenuRequest body)
    {
        var id = Guid.Parse(menuId);
        var e = await _db.SysMenus.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Menu {menuId} not found");
        if (body.Title != null) e.Title = body.Title;
        if (body.Path != null) e.Path = body.Path;
        if (body.Icon != null) e.Icon = body.Icon;
        if (Guid.TryParse(body.ParentId.ToString(), out var pid)) e.ParentId = pid;
        e.Type = (short)body.Type;
        e.SortOrder = body.SortOrder;
        e.Status = (short)body.Status;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task MenusDelete(string clientId, string menuId)
    {
        var id = Guid.Parse(menuId);
        var e = await _db.SysMenus.FirstOrDefaultAsync(m => m.Id == id);
        if (e != null)
        {
            _db.SysMenus.Remove(e);
            await _db.SaveChangesAsync();
        }
    }

    public override async Task<ApiSysMenu> Parent(string clientId, string menuId, Body2 body)
    {
        var id = Guid.Parse(menuId);
        var e = await _db.SysMenus.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Menu {menuId} not found");
        e.ParentId = string.IsNullOrEmpty(body.ParentId) ? Guid.Empty : Guid.Parse(body.ParentId);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ICollection<ApiSysMenu>> Reorder(string clientId, string menuId, ReorderSysMenuRequest body)
    {
        for (var i = 0; i < body.OrderedMenuIds.Count; i++)
        {
            var mid = Guid.Parse(body.OrderedMenuIds[i]);
            var row = await _db.SysMenus.FirstOrDefaultAsync(m => m.Id == mid && m.ClientId == clientId);
            if (row != null) row.SortOrder = i;
        }
        await _db.SaveChangesAsync();
        var rows = await _db.SysMenus
            .Where(m => m.ClientId == clientId)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Title)
            .ToListAsync();
        return rows.Select(ToDto).ToList();
    }
}
