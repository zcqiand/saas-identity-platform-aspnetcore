using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbRole = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysRole;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
// alias to disambiguate DTO SysRoleMenu from entity (no entity equivalent — pure DTO)
using ApiRoleMenu = Saas.Identity.AspNetCore.Controllers.Generated.SysRoleMenu;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F04 角色菜单授权（tenant-scoped）。
/// v0.5.0 NSwag 重 emit：RoleMenuGrant DTO → SysRoleMenu（直接反映 sys_role_menu 表）；
/// 端点全部加 clientId 必填 query；EF skip nav（SysRole.Menus）保留不变。
/// </summary>
public class TenantRoleMenusController : TenantRoleMenusControllerBase
{
    private readonly TenantGuard _guard;
    private readonly AppDbContext _db;

    public TenantRoleMenusController(TenantGuard guard, AppDbContext db)
    {
        _guard = guard;
        _db = db;
    }

    public override async Task<ICollection<ApiRoleMenu>> MenusGet(string tenantId, string roleId, string clientId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var role = await _db.SysRoles
            .Include(r => r.Menus)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"role {roleId} not found");
        return role.Menus.Select(m => new ApiRoleMenu
        {
            RoleId = role.Id,
            MenuId = m.Id,
        }).ToList();
    }

    public override async Task<ICollection<ApiRoleMenu>> MenusPut(string tenantId, string roleId, string clientId, SetSysRoleMenusRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var role = await _db.SysRoles
            .Include(r => r.Menus)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"role {roleId} not found");

        var menuIds = (body.MenuIds ?? new List<string>())
            .Where(m => Guid.TryParse(m, out _))
            .Select(m => Guid.Parse(m))
            .ToHashSet();

        // EF skip nav：清空旧 nav，再加新的（AppDbContext HasMany.UsingEntity 自动维护 junction）
        role.Menus.Clear();
        if (menuIds.Count > 0)
        {
            var newMenus = await _db.SysMenus.Where(m => menuIds.Contains(m.Id)).ToListAsync();
            foreach (var m in newMenus) role.Menus.Add(m);
        }
        role.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return role.Menus.Select(m => new ApiRoleMenu
        {
            RoleId = role.Id,
            MenuId = m.Id,
        }).ToList();
    }

    public override async Task MenusDelete(string tenantId, string roleId, string clientId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var role = await _db.SysRoles
            .Include(r => r.Menus)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (role == null) return;
        role.Menus.Clear();
        role.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}