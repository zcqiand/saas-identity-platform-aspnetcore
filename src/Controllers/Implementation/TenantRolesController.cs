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

    private static ApiRole ToDto(DbRole e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        Code = e.Code,
        Name = e.Name,
        Description = e.Description,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

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
        return new Response10
        {
            Items = items.Select(ToDto).ToList(),
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
        return ToDto(e);
    }

    public override async Task<ApiRole> RolesGet(string tenantId, string roleId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var e = await _db.Roles.FirstAsync(r => r.Id == id);
        return ToDto(e);
    }

    public override async Task<ApiRole> RolesPatch(string tenantId, string roleId, UpdateRoleRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var e = await _db.Roles.FirstAsync(r => r.Id == id);
        if (body.Name != null) e.Name = body.Name;
        if (body.Description != null) e.Description = body.Description;
        await _db.SaveChangesAsync();
        return ToDto(e);
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
        return ToDto(role);
    }
}