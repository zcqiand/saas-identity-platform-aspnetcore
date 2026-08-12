using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M05.F01 API Key 生命周期（tenant-scoped）。
/// 每个 tenant-scoped 方法第一行调 _tenantGuard.VerifyPathTenant(tenantId)。
/// </summary>
public class TenantApiKeysController : TenantApiKeysControllerBase
{
    private readonly TenantGuard _guard;
    public TenantApiKeysController(TenantGuard guard) { _guard = guard; }

    public override Task<Response4> ApiKeysGet(string tenantId, int? page, int? pageSize)
    {
        // M05.F01.I01 API Key 列表
        _guard.VerifyPathTenant(tenantId);
        var gid = Guid.Parse(tenantId);
        var items = InMemoryStore.ApiKeys.Where(k => k.TenantId == gid).ToList();
        return Task.FromResult(new Response4
        {
            Items = items,
            Page = page ?? 1,
            PageSize = pageSize ?? items.Count,
            Total = items.Count,
        });
    }

    public override Task<CreateApiKeyResponse> ApiKeysPost(string tenantId, CreateApiKeyRequest body)
    {
        // M05.F01.I02 创建 API Key
        _guard.VerifyPathTenant(tenantId);
        var gid = Guid.Parse(tenantId);
        var k = new ApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = gid,
            Name = body.Name,
            Prefix = "sk_live",
            Status = ApiKeyStatus.Active,
        };
        InMemoryStore.ApiKeys.Add(k);
        return Task.FromResult(new CreateApiKeyResponse
        {
            ApiKey = k,
            Secret = "sk_live_" + Guid.NewGuid().ToString("N").Substring(0, 24),
        });
    }

    public override Task<ApiKey> Revoke(string tenantId, string keyId)
    {
        // M05.F01.I03 吊销 API Key
        _guard.VerifyPathTenant(tenantId);
        var k = InMemoryStore.ApiKeys.First(x => x.Id == Guid.Parse(keyId));
        k.Status = ApiKeyStatus.Revoked;
        return Task.FromResult(k);
    }

    public override Task<CreateApiKeyResponse> Rotate(string tenantId, string keyId)
    {
        // M05.F01.I04 轮换 API Key
        _guard.VerifyPathTenant(tenantId);
        var k = InMemoryStore.ApiKeys.First(x => x.Id == Guid.Parse(keyId));
        k.Id = Guid.NewGuid();
        return Task.FromResult(new CreateApiKeyResponse
        {
            ApiKey = k,
            Secret = "sk_live_" + Guid.NewGuid().ToString("N").Substring(0, 24),
        });
    }
}