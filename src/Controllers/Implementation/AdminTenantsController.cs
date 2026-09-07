using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbTenant = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.Tenant;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
// alias 避免与 NSwag-generated DTO `Tenant` 冲突
using ApiTenant = Saas.Identity.AspNetCore.Controllers.Generated.Tenant;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F01 租户 CRUD（平台 admin）。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// v0.5.0：9/7 重组 — Tenant 表 Settings 列已 DROP（jsonb 字段进 tenant_settings
/// 单独表，本期未实现），DTO TenantSettings 字段忽略。Code → TenantKey（重命名），
/// Status 从 string enum 改为 smallint（1=active / 2=suspended / 3=archived）。
/// </summary>
public class AdminTenantsController : AdminTenantsControllerBase
{
    private readonly AppDbContext _db;

    public AdminTenantsController(AppDbContext db) { _db = db; }

    private static ApiTenant ToDto(DbTenant e) => new()
    {
        Id = e.Id,
        Code = e.TenantKey,
        Name = e.Name,
        Status = ToDtoStatus(e.Status),
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
        // Settings: tenant_settings 表未实现，DTO 字段返回空对象占位
        Settings = new TenantSettings(),
    };

    private static TenantStatus ToDtoStatus(short s) => s switch
    {
        2 => TenantStatus.Suspended,
        3 => TenantStatus.Archived,
        _ => TenantStatus.Active,
    };

    private static short ToDbStatus(TenantStatus s) => s switch
    {
        TenantStatus.Suspended => 2,
        TenantStatus.Archived => 3,
        _ => 1,
    };

    public override async Task<Response2> TenantsGet(int? page, int? pageSize)
    {
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        var items = await _db.Tenants.OrderByDescending(t => t.CreatedAt)
            .Skip(p * ps).Take(ps).ToListAsync();
        var total = await _db.Tenants.CountAsync();
        return new Response2
        {
            Items = items.Select(ToDto).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<ApiTenant> TenantsPost(CreateTenantRequest body)
    {
        var e = new DbTenant
        {
            Id = Guid.NewGuid(),
            TenantKey = body.Code,
            Name = body.Name,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Tenants.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ApiTenant> TenantsGet(string id)
    {
        var gid = Guid.Parse(id);
        var e = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == gid)
            ?? throw new KeyNotFoundException($"tenant {id} not found");
        return ToDto(e);
    }

    public override async Task<ApiTenant> TenantsPatch(string id, UpdateTenantRequest body)
    {
        var gid = Guid.Parse(id);
        var e = await _db.Tenants.FirstAsync(t => t.Id == gid);
        if (body.Name != null) e.Name = body.Name;
        if (body.Code != null) e.TenantKey = body.Code;
        e.Status = ToDbStatus(body.Status);
        e.UpdatedAt = DateTime.UtcNow;
        // Settings: tenant_settings 表未实现，PATCH 不持久化（DTO 字段忽略）
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task TenantsDelete(string id)
    {
        var gid = Guid.Parse(id);
        var e = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == gid);
        if (e == null) throw new KeyNotFoundException($"tenant {id} not found");
        _db.Tenants.Remove(e);
        await _db.SaveChangesAsync();
        Response.StatusCode = StatusCodes.Status204NoContent;
    }
}