using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Services;
using DbApiKey = Saas.Identity.AspNetCore.Domain.Entities.ApiKey;

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
    private readonly IAuditWriter _audit;
    private readonly IHttpContextAccessor _http;

    public TenantApiKeysController(
      TenantGuard guard,
      AppDbContext db,
      IAuditWriter audit,
      IHttpContextAccessor http)
    {
        _guard = guard;
        _db = db;
        _audit = audit;
        _http = http;
    }

    private Guid? CallerUserId()
    {
        var sub = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? _http.HttpContext?.User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
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
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        var items = await _db.ApiKeys
            .Where(k => k.TenantId == tid)
            .OrderByDescending(k => k.CreatedAt)
            .Skip(p * ps).Take(ps)
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
        var caller = CallerUserId();
        await _audit.WriteAsync(
          tenantId,
          caller?.ToString(),
          "api_key_created",
          targetUserId: null,
          new Dictionary<string, object?> { ["apiKeyId"] = entity.Id.ToString() });
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
        var caller = CallerUserId();
        await _audit.WriteAsync(
          tenantId,
          caller?.ToString(),
          "api_key_revoked",
          targetUserId: null,
          new Dictionary<string, object?> { ["apiKeyId"] = e.Id.ToString() });
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
        var caller = CallerUserId();
        await _audit.WriteAsync(
          tenantId,
          caller?.ToString(),
          "api_key_revoked",
          targetUserId: null,
          new Dictionary<string, object?> { ["apiKeyId"] = old.Id.ToString() });
        await _audit.WriteAsync(
          tenantId,
          caller?.ToString(),
          "api_key_created",
          targetUserId: null,
          new Dictionary<string, object?> { ["apiKeyId"] = newEntity.Id.ToString() });
        return new CreateApiKeyResponse
        {
            ApiKey = ToDto(newEntity),
            Secret = secret,
        };
    }

    // M05.F01.I05 物理删除（区别于 I03 revoke 软删：直接 DELETE FROM api_keys，无审计事件）
    // 与 I03 revoke 并存：revoke 保留行（status=revoked + revokedAt）；本 op 行消失。
    // 幂等：重复删已不存在的 keyId 抛 InvalidOperationException（FirstAsync 未找到），global handler → 404。
    // @entry M05.F01.I05
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public override async Task ApiKeysDelete(string tenantId, string keyId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(keyId);
        var e = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id);
        if (e == null)
        {
            // 2026-08-31 contract-test I21：重复 DELETE 期望 404（幂等性）而非 500。
            // FirstAsync 抛 InvalidOperationException 落全局 catch → 500。
            // 改 FirstOrDefaultAsync 后显式抛 KeyNotFoundException，由 Program.cs 映射 404。
            throw new KeyNotFoundException($"api key {keyId} not found");
        }
        _db.ApiKeys.Remove(e);
        await _db.SaveChangesAsync();
        // NSwag 生成的 abstract 签名是 Task（非 Task<IActionResult>）—— ASP.NET Core 框架
        // 对 Task DELETE action 不会自动返 204（默认 200 空 body）。OpenAPI 契约要求 204，
        // 显式置状态码以满足 contract-test I21（参照 msw + springboot + nextjs 三家均返 204）。
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
        // 不写 audit event（物理删不留痕；与 revoke 写 api_key_revoked 形成对照）
    }
}