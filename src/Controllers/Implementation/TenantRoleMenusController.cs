using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbRole = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysRole;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F04 角色菜单授权（tenant-scoped）。
/// 2026-09-10 I20 方案 C：GET/PUT 切 RoleMenuGrant 聚合返回（{roleId, tenantId, menuIds[], updatedAt}，
/// 与 msw/nextjs 对齐）；SysRoleMenu 行 DTO 已从 shared 契约移除。
/// v0.5.0 端点 clientId 必填 query；EF skip nav（SysRole.Menus）保留不变。
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

    public override async Task<RoleMenuGrant> MenusGet(string tenantId, string roleId, string? clientId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var role = await _db.SysRoles
            .Include(r => r.Menus)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"role {roleId} not found");
        return ToGrant(role);
    }

    public override async Task<RoleMenuGrant> MenusPut(string tenantId, string roleId, string? clientId, SetSysRoleMenusRequest body)
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
        role.UpdatedAt = DateTime.UtcNow; // touch — 聚合 updatedAt 来源（家族约定）
        await _db.SaveChangesAsync();
        return ToGrant(role);
    }

    public override async Task MenusDelete(string tenantId, string roleId, string? clientId)
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

    private static RoleMenuGrant ToGrant(DbRole role) => new()
    {
        RoleId = role.Id,
        TenantId = role.TenantId,
        MenuIds = role.Menus.Select(m => m.Id.ToString()).ToList(),
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(role.UpdatedAt, DateTimeKind.Utc)),
    };
}
