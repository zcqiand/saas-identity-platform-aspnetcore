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
/// v0.5.0 NSwag 重 emit：Tenant.Code → TenantKey（字段重命名）；
/// Tenant.Settings 字段 9/7 重组 DROP（迁 tenant_settings 单独表未实现）；
/// CreateTenantRequest / UpdateTenantRequest 字段名同步重命名。
/// </summary>
public class AdminTenantsController : AdminTenantsControllerBase
{
    private readonly AppDbContext _db;

    public AdminTenantsController(AppDbContext db) { _db = db; }

    private static ApiTenant ToDto(DbTenant e) => new()
    {
        Id = e.Id,
        TenantKey = e.TenantKey,
        Name = e.Name,
        Status = StatusEnumMaps.MapTenantStatus(e.Status), // DB 1=active, 2=suspended（裸 cast off-by-one 已修）
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc)),
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(e.UpdatedAt, DateTimeKind.Utc)),
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
            TenantKey = body.TenantKey,
            Name = body.Name,
            Status = 1, // active
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
        // 5.26 partial-update 语义（5.18 先例 333d8ac 同款）：PATCH 不带 status = 保持
        // 原值，不再覆写回 Active。
        if (body.Status.HasValue) e.Status = StatusEnumMaps.ToDbTenantStatus(body.Status.Value);
        e.UpdatedAt = DateTime.UtcNow;
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