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
        // 2026-09-12 修复：DTO SysRole.Status 是 int（契约层无枚举），漏映射恒默认 0；
        // DB smallint 直传（1=active，msw/seed 同值）。
        Status = e.Status,
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc)),
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(e.UpdatedAt, DateTimeKind.Utc)),
    };

    public override async Task<Response6> RolesGet(string tenantId, string? clientId, int? page, int? pageSize)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        // 2026-09-12 修复：clientId 空串（?clientId=）不再过滤（此前生成 WHERE FALSE → 恒空）
        var q = string.IsNullOrEmpty(clientId)
            ? _db.SysRoles.Where(r => r.TenantId == tid)
            : _db.SysRoles.Where(r => r.TenantId == tid && r.ClientId == clientId);
        // 2026-09-12 四方 live 修复（roles list 排序）：msw oracle 返回插入序（created_at ASC），
        // 此前 DESC 让 normalize 后第 5 行起与 oracle 分叉；显式 created_at ASC, id ASC 对齐。
        var items = await q.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Skip(p * ps).Take(ps).ToListAsync();
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
        // 2026-09-12：role 单条寻址必须校验归属租户（对齐 springboot findRoleInTenant，
        // 否则租户 A 路径可读租户 B 的 role——跨租户越权）
        var tid = Guid.Parse(tenantId);
        var e = await _db.SysRoles.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tid)
            ?? throw new KeyNotFoundException("Role not found");
        return ToDto(e);
    }

    public override async Task<ApiRole> RolesPatch(string tenantId, string roleId, UpdateSysRoleRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        // 2026-09-12：同 RolesGet，patch/delete 也按 (id, tenantId) 双键寻址
        var tid = Guid.Parse(tenantId);
        var e = await _db.SysRoles.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tid)
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
        // 2026-09-12：同 RolesGet，delete 按 (id, tenantId) 双键寻址
        var tid = Guid.Parse(tenantId);
        var e = await _db.SysRoles.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tid);
        if (e != null)
        {
            _db.SysRoles.Remove(e);
            await _db.SaveChangesAsync();
        }
    }
}