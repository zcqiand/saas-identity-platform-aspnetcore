using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M05.F01 API Key 生命周期（tenant-scoped）。
/// </summary>
public class TenantApiKeysControllerTests : TestBase
{
    private TenantApiKeysController NewC()
    {
        var ctx = new StubTenantContext { TenantId = InMemoryStore.AcmeId.ToString() };
        return new TenantApiKeysController(new TenantGuard(ctx));
    }

    [Fact]
    [Trait("Fn", "M05.F01.I01")]
    public async Task List_returnsApiKeysInTenant()
    {
        var c = NewC();
        var res = await c.ApiKeysGet(InMemoryStore.AcmeId.ToString(), null, null);
        Assert.NotEmpty(res.Items);
    }

    [Fact]
    [Trait("Fn", "M05.F01.I02")]
    public async Task Create_returnsApiKeyAndSecret()
    {
        var c = NewC();
        var res = await c.ApiKeysPost(InMemoryStore.AcmeId.ToString(), new() { Name = "New Key" });
        Assert.NotEqual(Guid.Empty, res.ApiKey.Id);
        Assert.False(string.IsNullOrEmpty(res.Secret));
    }

    [Fact]
    [Trait("Fn", "M05.F01.I03")]
    public async Task Revoke_setsStatusRevoked()
    {
        var c = NewC();
        var k = await c.Revoke(InMemoryStore.AcmeId.ToString(), InMemoryStore.ApiKeyId.ToString());
        Assert.Equal(ApiKeyStatus.Revoked, k.Status);
    }

    [Fact]
    [Trait("Fn", "M05.F01.I04")]
    public async Task Rotate_returnsNewApiKey()
    {
        var c = NewC();
        var before = InMemoryStore.ApiKeyId;
        var res = await c.Rotate(InMemoryStore.AcmeId.ToString(), InMemoryStore.ApiKeyId.ToString());
        Assert.NotEqual(before, res.ApiKey.Id);
    }
}