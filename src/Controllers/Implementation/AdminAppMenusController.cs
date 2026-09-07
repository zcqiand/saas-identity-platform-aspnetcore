using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbMenu = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysMenu;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
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
        AppId = Guid.Parse(e.ClientId),
        ParentId = e.ParentId,
        Code = e.Path ?? "",
        Name = e.Title,
        Path = e.Path,
        Icon = e.Icon,
        Type = ToDtoType(e.Type),
        SortOrder = e.SortOrder,
        Status = ToDtoStatus(e.Status),
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.CreatedAt,
    };

    // SysMenu.type / status 现在是 short（PG native enum → smallint，9/7 重组后），
    // 具体枚举值与 shared/src/db/seed.ts 对齐（约定：group=1 / page=2 / action=3,
    // active=1 / disabled=0）。DTO 端用 NSwag 生成的 MenuType / MenuStatus enum。
    private static MenuType ToDtoType(short s) => s switch
    {
        1 => MenuType.Group,
        2 => MenuType.Page,
        _ => MenuType.Action,
    };

    private static short ToDbType(MenuType t) => t switch
    {
        MenuType.Group => 1,
        MenuType.Page => 2,
        _ => 3,
    };

    private static MenuStatus ToDtoStatus(short s) => s switch
    {
        1 => MenuStatus.Active,
        _ => MenuStatus.Disabled,
    };

    private static short ToDbStatus(MenuStatus s) => s switch
    {
        MenuStatus.Active => 1,
        _ => 0,
    };

    // === endpoints ===

    public override async Task<ICollection<ApiMenu>> MenusGet(string appId)
    {
        var aid = await ResolveAppIdAsync(appId);
        var rows = await _db.SysMenus.Where(m => m.ClientId == aid.ToString().ToString()).ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    public override async Task<ApiMenu> MenusPost(string appId, CreateMenuRequest body)
    {
        var aid = await ResolveAppIdAsync(appId);
        var e = new DbMenu
        {
            Id = Guid.NewGuid(),
            ClientId = aid.ToString(), // 9/7 重组：AppId (Guid) → ClientId (string)
            // Scaffold SysMenu.ParentId 是 Guid（非 nullable，DB 默认 00000000-...）
            // Guid.Empty 表示顶级菜单
            ParentId = body.ParentId,
            Title = body.Name, // 9/7 重组：menu.name → menu.title
            Path = body.Path,
            Icon = body.Icon,
            Type = ToDbType(body.Type),
            SortOrder = body.SortOrder,
            Status = ToDbStatus(body.Status),
            // Scaffold SysMenu.CreatedAt 是 DateTime（PG timestamptz 映射）
            CreatedAt = DateTime.UtcNow,
        };
        _db.SysMenus.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ApiMenu> MenusGet(string appId, string menuId)
    {
        var id = Guid.Parse(menuId);
        // 2026-09-01 contract-test I52：不存在 id → 404（FirstAsync 抛 → 500）
        var e = await _db.SysMenus.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Menu {menuId} not found");
        return ToDto(e);
    }

    public override async Task<ApiMenu> MenusPatch(string appId, string menuId, UpdateMenuRequest body)
    {
        var id = Guid.Parse(menuId);
        var e = await _db.SysMenus.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Menu {menuId} not found");
        if (body.Name != null) e.Title = body.Name;
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
        var e = await _db.SysMenus.FirstOrDefaultAsync(m => m.Id == id);
        if (e != null)
        {
            _db.SysMenus.Remove(e);
            await _db.SaveChangesAsync();
        }
    }

    public override async Task<ApiMenu> Parent(string appId, string menuId, Body body)
    {
        var id = Guid.Parse(menuId);
        var e = await _db.SysMenus.FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Menu {menuId} not found");
        // ParentId 是 non-nullable Guid；空字符串 → Guid.Empty（顶级菜单）
        e.ParentId = string.IsNullOrEmpty(body.ParentId) ? Guid.Empty : Guid.Parse(body.ParentId);
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
            var row = await _db.SysMenus.FirstOrDefaultAsync(m => m.Id == mid && m.ClientId == aid.ToString().ToString());
            if (row != null)
            {
                row.SortOrder = i;
                // SysMenu 无 UpdatedAt 列（9/7 重组）；sortOrder 改了 CreatedAt 也行（DTO 兼容）;
            }
        }
        await _db.SaveChangesAsync();
        var rows = await _db.SysMenus
            .Where(m => m.ClientId == aid.ToString())
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Title)
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
            var existsById = await _db.OauthClients.AnyAsync(a => a.Id == gid);
            if (existsById) return gid;
        }
        var byCode = await _db.OauthClients.FirstOrDefaultAsync(a => a.ClientId == appIdOrCode)
            ?? throw new KeyNotFoundException($"app '{appIdOrCode}' not found");
        return byCode.Id;
    }
}