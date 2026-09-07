using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F04 角色菜单授权（tenant-scoped）。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// v0.5.0：sys_role_menu 走 EF skip nav（scaffold config in AppDbContext.cs:351），
/// 不再手写 junction entity DbGrant/RolesGrants（Domain/Entities 已废）。
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

    public override async Task<RoleMenuGrant> MenusGet(string tenantId, string roleId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var role = await _db.SysRoles
            .Include(r => r.Menus)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"role {roleId} not found");
        return new RoleMenuGrant
        {
            RoleId = id,
            TenantId = Guid.Parse(tenantId),
            MenuIds = role.Menus.Select(m => m.Id.ToString()).ToList(),
            UpdatedAt = role.UpdatedAt,
        };
    }

    public override async Task<RoleMenuGrant> MenusPut(string tenantId, string roleId, SetRoleMenusRequest body)
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

        // 用 EF skip nav 重写整批 sys_role_menu：清空旧 nav，再加新的
        // (AppDbContext HasMany.UsingEntity<SysRoleMenu> 自动维护 junction)
        role.Menus.Clear();
        if (menuIds.Count > 0)
        {
            var newMenus = await _db.SysMenus.Where(m => menuIds.Contains(m.Id)).ToListAsync();
            foreach (var m in newMenus) role.Menus.Add(m);
        }
        role.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new RoleMenuGrant
        {
            RoleId = id,
            TenantId = Guid.Parse(tenantId),
            MenuIds = menuIds.Select(g => g.ToString()).ToList(),
            UpdatedAt = role.UpdatedAt,
        };
    }

    public override async Task MenusDelete(string tenantId, string roleId)
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