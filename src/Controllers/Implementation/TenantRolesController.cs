using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbRole = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysRole;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
// alias to disambiguate DTO SysRole (NSwag) from entity DbRole (scaffold)
using ApiRole = Saas.Identity.AspNetCore.Controllers.Generated.SysRole;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F03 租户角色 CRUD。
/// v0.5.0 NSwag 重 emit：Role → SysRole（DTO + entity 都用 SysRole 名）；
/// CreateRoleRequest → CreateSysRoleRequest；UpdateRoleRequest → UpdateSysRoleRequest；
/// RolesGet 加 clientId 必填 query；Permissions 端点 9/7 重组后已废（Permission 表 DROP）。
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
        ClientId = e.ClientId,
        RoleCode = e.RoleCode,
        RoleName = e.RoleName,
        Description = e.Description,
        IsPreset = e.IsPreset,
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc)),
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(e.UpdatedAt, DateTimeKind.Utc)),
    };

    public override async Task<Response6> RolesGet(string tenantId, string clientId, int? page, int? pageSize)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        // 2026-09-12 修复：clientId 空串（?clientId=）不再过滤（此前生成 WHERE FALSE → 恒空）
        var q = string.IsNullOrEmpty(clientId)
            ? _db.SysRoles.Where(r => r.TenantId == tid)
            : _db.SysRoles.Where(r => r.TenantId == tid && r.ClientId == clientId);
        var items = await q.OrderByDescending(r => r.CreatedAt).Skip(p * ps).Take(ps).ToListAsync();
        var total = await q.CountAsync();
        return new Response6
        {
            Items = items.Select(ToDto).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<ApiRole> RolesPost(string tenantId, CreateSysRoleRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var e = new DbRole
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse(tenantId),
            ClientId = body.ClientId,
            RoleCode = body.RoleCode,
            RoleName = body.RoleName,
            Description = body.Description,
            IsPreset = false,
            Status = 1,
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
            ?? throw new KeyNotFoundException("Role not found");
        return ToDto(e);
    }

    public override async Task<ApiRole> RolesPatch(string tenantId, string roleId, UpdateSysRoleRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var e = await _db.SysRoles.FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException("Role not found");
        if (body.RoleName != null) e.RoleName = body.RoleName;
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