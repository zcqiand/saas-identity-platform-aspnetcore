using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbRole = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysRole;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;

// alias 避免与 NSwag-generated DTO `Role` 冲突
using ApiRole = Saas.Identity.AspNetCore.Controllers.Generated.Role;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F03 租户角色 CRUD（tenant-scoped）+ sys_role_menu 菜单授权委派。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// v0.5.0：M02.F02 权限矩阵已废（Permission / RolePermission / Permissions 表 DROP）——
/// permissionIds 字段先保留为 DTO 兼容性占位（contract-test M96.F02.I07/I08 仍断言
/// 字段存在），实际永远返回空数组。菜单授权由 TenantRoleMenusController 接管
/// （M00.F04 sys_role_menu M:N）。
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
        Code = e.RoleCode,
        Name = e.RoleName,
        // permissionIds DTO 字段保留兼容性占位（Permission 表已 DROP，恒空）
        PermissionIds = new List<string>(),
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    public override async Task<Response10> RolesGet(string tenantId, int? page, int? pageSize)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        var q = _db.SysRoles.Where(r => r.TenantId == tid);
        var items = await q.OrderByDescending(r => r.CreatedAt)
            .Skip(p * ps).Take(ps).ToListAsync();
        return new Response10
        {
            Items = items.Select(ToDto).ToList(),
            Page = p,
            PageSize = ps,
            Total = await q.CountAsync(),
        };
    }

    // M02.F02 权限矩阵已废：Permission / RolePermission 表 DROP，此端点保留路由但
    // no-op 返回空。客户端调用会收到空 permissionIds（兼容 contract-test 字段断言）。
    public override Task<ApiRole> Permissions(string tenantId, string roleId, Body5 body)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        return Task.FromResult(new ApiRole
        {
            Id = id,
            TenantId = Guid.Parse(tenantId),
            PermissionIds = new List<string>(),
        });
    }

    public override async Task<ApiRole> RolesPost(string tenantId, CreateRoleRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var e = new DbRole
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse(tenantId),
            RoleCode = body.Code,
            RoleName = body.Name,
            Description = body.Description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.SysRoles.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ApiRole> RolesGet(string tenantId, string roleId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var e = await _db.SysRoles.FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"Role not found");
        return ToDto(e);
    }

    public override async Task<ApiRole> RolesPatch(string tenantId, string roleId, UpdateRoleRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var e = await _db.SysRoles.FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"Role not found");
        if (body.Name != null) e.RoleName = body.Name;
        if (body.Description != null) e.Description = body.Description;
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task RolesDelete(string tenantId, string roleId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var e = await _db.SysRoles.FirstOrDefaultAsync(r => r.Id == id);
        if (e != null)
        {
            _db.SysRoles.Remove(e);
            await _db.SaveChangesAsync();
        }
    }
}