using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using DbApiKey = Saas.Identity.AspNetCore.Domain.Entities.ApiKey;
using Saas.Identity.AspNetCore.Security;

// alias 避免与 NSwag-generated DTO `ApiKey` 冲突
using ApiKeyDto = Saas.Identity.AspNetCore.Controllers.Generated.ApiKey;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M05.F01 API Key 生命周期（tenant-scoped）。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// </summary>
public class TenantApiKeysController : TenantApiKeysControllerBase
{
    private readonly TenantGuard _guard;
    private readonly AppDbContext _db;

    public TenantApiKeysController(TenantGuard guard, AppDbContext db)
    {
        _guard = guard;
        _db = db;
    }

    private static ApiKeyDto ToDto(DbApiKey e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        Name = e.Name,
        Prefix = e.Prefix,
        Status = ToDtoStatus(e.Status),
        Scopes = e.Scopes,
        CreatedAt = e.CreatedAt,
        LastUsedAt = e.LastUsedAt ?? default,
        ExpiresAt = e.ExpiresAt ?? default,
        RevokedAt = e.RevokedAt ?? default,
    };

    private static ApiKeyStatus ToDtoStatus(string s) => s switch
    {
        "active" => ApiKeyStatus.Active,
        "revoked" => ApiKeyStatus.Revoked,
        _ => ApiKeyStatus.Expired,
    };

    private static string ToDbStatus(ApiKeyStatus s) => s switch
    {
        ApiKeyStatus.Active => "active",
        ApiKeyStatus.Revoked => "revoked",
        _ => "expired",
    };

    public override async Task<Response4> ApiKeysGet(string tenantId, int? page, int? pageSize)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var p = page ?? 1;
        var ps = pageSize ?? 20;
        var items = await _db.ApiKeys
            .Where(k => k.TenantId == tid)
            .OrderByDescending(k => k.CreatedAt)
            .Skip((p - 1) * ps).Take(ps)
            .ToListAsync();
        var total = await _db.ApiKeys.CountAsync(k => k.TenantId == tid);
        return new Response4
        {
            Items = items.Select(ToDto).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<CreateApiKeyResponse> ApiKeysPost(string tenantId, CreateApiKeyRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var prefix = "sk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant().Substring(0, 8);
        var secret = "sk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var entity = new DbApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse(tenantId),
            Name = body.Name,
            Prefix = prefix,
            SecretHash = $"plain:{secret}",
            Status = "active",
            Scopes = body.Scopes?.ToList() ?? new(),
            ExpiresAt = body.ExpiresAt,
        };
        _db.ApiKeys.Add(entity);
        await _db.SaveChangesAsync();
        return new CreateApiKeyResponse
        {
            ApiKey = ToDto(entity),
            Secret = secret,
        };
    }

    public override async Task<ApiKeyDto> Revoke(string tenantId, string keyId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(keyId);
        var e = await _db.ApiKeys.FirstAsync(k => k.Id == id);
        e.Status = "revoked";
        e.RevokedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<CreateApiKeyResponse> Rotate(string tenantId, string keyId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(keyId);
        var old = await _db.ApiKeys.FirstAsync(k => k.Id == id);
        // mark old revoked
        old.Status = "revoked";
        old.RevokedAt = DateTimeOffset.UtcNow;
        // create new with same name + scopes
        var prefix = "sk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant().Substring(0, 8);
        var secret = "sk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var newEntity = new DbApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = old.TenantId,
            Name = old.Name,
            Prefix = prefix,
            SecretHash = $"plain:{secret}",
            Status = "active",
            Scopes = old.Scopes,
            ExpiresAt = old.ExpiresAt,
        };
        _db.ApiKeys.Add(newEntity);
        await _db.SaveChangesAsync();
        return new CreateApiKeyResponse
        {
            ApiKey = ToDto(newEntity),
            Secret = secret,
        };
    }
}