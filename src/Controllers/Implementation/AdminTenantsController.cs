using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using DbTenant = Saas.Identity.AspNetCore.Domain.Entities.Tenant;
// alias 避免与 NSwag-generated DTO `Tenant` 冲突
using ApiTenant = Saas.Identity.AspNetCore.Controllers.Generated.Tenant;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F01 租户 CRUD（平台 admin）。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// </summary>
public class AdminTenantsController : AdminTenantsControllerBase
{
    private readonly AppDbContext _db;

    public AdminTenantsController(AppDbContext db) { _db = db; }

    // === DTO ↔ Entity 转换 ===

    private static ApiTenant ToDto(DbTenant e) => new()
    {
        Id = e.Id,
        Code = e.Code,
        Name = e.Name,
        Status = ToDtoStatus(e.Status),
        Settings = ToSettingsDto(e.Settings),
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    private static TenantSettings ToSettingsDto(Dictionary<string, object?> src)
    {
        var s = new TenantSettings();
        if (src.TryGetValue("themeColor", out var tc)) s.ThemeColor = tc?.ToString() ?? "";
        if (src.TryGetValue("locale", out var lo)) s.Locale = lo?.ToString() ?? "";
        if (src.TryGetValue("maxUsers", out var mu) && mu != null) s.MaxUsers = Convert.ToInt32(mu);
        foreach (var kv in src)
        {
            if (kv.Key is "themeColor" or "locale" or "maxUsers") continue;
            s.AdditionalProperties[kv.Key] = kv.Value;
        }
        return s;
    }

    private static TenantStatus ToDtoStatus(string s) => s switch
    {
        "active" => TenantStatus.Active,
        "suspended" => TenantStatus.Suspended,
        _ => TenantStatus.Archived,
    };

    private static string ToDbStatus(TenantStatus s) => s switch
    {
        TenantStatus.Active => "active",
        TenantStatus.Suspended => "suspended",
        _ => "archived",
    };

    // === endpoints ===

    public override async Task<Response2> TenantsGet(int? page, int? pageSize)
    {
        var p = page ?? 1;
        var ps = pageSize ?? 20;
        var items = await _db.Tenants.OrderByDescending(t => t.CreatedAt)
            .Skip((p - 1) * ps).Take(ps).ToListAsync();
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
            Code = body.Code,
            Name = body.Name,
            Status = "active",
            Settings = body.Settings == null ? new() : new Dictionary<string, object?>
            {
                ["themeColor"] = body.Settings.ThemeColor,
                ["locale"] = body.Settings.Locale,
                ["maxUsers"] = body.Settings.MaxUsers,
            }.Concat(body.Settings.AdditionalProperties)
              .ToDictionary(kv => kv.Key, kv => kv.Value),
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
        if (body.Code != null) e.Code = body.Code;
        e.Status = ToDbStatus(body.Status);
        if (body.Settings != null)
        {
            e.Settings = new Dictionary<string, object?>
            {
                ["themeColor"] = body.Settings.ThemeColor,
                ["locale"] = body.Settings.Locale,
                ["maxUsers"] = body.Settings.MaxUsers,
            }.Concat(body.Settings.AdditionalProperties)
              .ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task TenantsDelete(string id)
    {
        var gid = Guid.Parse(id);
        var e = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == gid);
        if (e != null)
        {
            _db.Tenants.Remove(e);
            await _db.SaveChangesAsync();
        }
    }
}