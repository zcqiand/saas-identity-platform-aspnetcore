using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using DbRole = Saas.Identity.AspNetCore.Domain.Entities.Role;
using DbRolePermission = Saas.Identity.AspNetCore.Domain.Entities.RolePermission;
using Saas.Identity.AspNetCore.Security;

// alias 避免与 NSwag-generated DTO `Role` 冲突
using ApiRole = Saas.Identity.AspNetCore.Controllers.Generated.Role;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M02.F01 角色 CRUD（tenant-scoped）+ M02.F02 权限矩阵。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// 2026-08-30：ToDto 加 PermissionIds（join RolePermissions → permissions.id），
///   contract-test M96.F02.I07/I08 要求 role 必含 permissionIds。
/// </summary>
public class TenantRolesController : TenantRolesControllerBase
{
    private readonly TenantGuard _guard;
    private readonly AppDbContext _db;

    public TenantRolesController(TenantGuard guard, AppDbContext db)
    {
        _guard = guard;
        _db = db;
    }

    private static ApiRole ToDto(DbRole e, IEnumerable<Guid> permissionIds) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        Code = e.Code,
        Name = e.Name,
        Description = e.Description,
        PermissionIds = permissionIds.Select(g => g.ToString()).ToList(),
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    /// <summary>单 role 的 permission UUID 列表（M:N join）。</summary>
    private Task<List<Guid>> PermissionIdsForRole(Guid roleId)
        => _db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionId)
            .ToListAsync();

    /// <summary>批量：roleId → permissionIds（避免 N+1）。</summary>
    private async Task<Dictionary<Guid, List<Guid>>> PermissionIdsForRoles(IReadOnlyCollection<Guid> roleIds)
    {
        if (roleIds.Count == 0) return new();
        var rows = await _db.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync();
        return rows.GroupBy(r => r.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PermissionId).ToList());
    }

    public override async Task<Response10> RolesGet(string tenantId, int? page, int? pageSize)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var p = page ?? 1;
        var ps = pageSize ?? 20;
        var q = _db.Roles.Where(r => r.TenantId == tid);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(r => r.CreatedAt)
            .Skip((p - 1) * ps).Take(ps).ToListAsync();
        var perms = await PermissionIdsForRoles(items.Select(r => r.Id).ToList());
        return new Response10
        {
            Items = items.Select(r => ToDto(r, perms.TryGetValue(r.Id, out var p) ? p : new())).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<ApiRole> RolesPost(string tenantId, CreateRoleRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var e = new DbRole
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse(tenantId),
            Code = body.Code,
            Name = body.Name,
            Description = body.Description,
        };
        _db.Roles.Add(e);
        await _db.SaveChangesAsync();
        // 新建角色无 permission
        return ToDto(e, Array.Empty<Guid>());
    }

    public override async Task<ApiRole> RolesGet(string tenantId, string roleId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var e = await _db.Roles.FirstAsync(r => r.Id == id);
        return ToDto(e, await PermissionIdsForRole(id));
    }

    public override async Task<ApiRole> RolesPatch(string tenantId, string roleId, UpdateRoleRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var e = await _db.Roles.FirstAsync(r => r.Id == id);
        if (body.Name != null) e.Name = body.Name;
        if (body.Description != null) e.Description = body.Description;
        await _db.SaveChangesAsync();
        // PATCH 不动 permission，返回当前快照
        return ToDto(e, await PermissionIdsForRole(id));
    }

    public override async Task RolesDelete(string tenantId, string roleId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var e = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (e != null)
        {
            _db.Roles.Remove(e);
            await _db.SaveChangesAsync();
        }
    }

    public override async Task<ApiRole> Permissions(string tenantId, string roleId, Body5 body)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        // 校验 role 存在
        var role = await _db.Roles.FirstAsync(r => r.Id == id);
        // 整批替换 role ↔ permission M:N
        var oldPerms = await _db.RolePermissions.Where(rp => rp.RoleId == id).ToListAsync();
        if (oldPerms.Count > 0) _db.RolePermissions.RemoveRange(oldPerms);
        var newPerms = (body.PermissionIds ?? new List<string>())
            .Where(p => Guid.TryParse(p, out _))
            .Select(p => new DbRolePermission { RoleId = id, PermissionId = Guid.Parse(p) })
            .ToList();
        if (newPerms.Count > 0) _db.RolePermissions.AddRange(newPerms);
        await _db.SaveChangesAsync();
        // 返回刚设置完的 permissionIds
        return ToDto(role, newPerms.Select(np => np.PermissionId).ToList());
    }
}