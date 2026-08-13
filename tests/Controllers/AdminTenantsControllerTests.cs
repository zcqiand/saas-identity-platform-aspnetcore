using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M00.F01 租户 CRUD（平台 admin）测试。
/// admin-level 不需 TenantGuard，直接调 controller。
/// </summary>
public class AdminTenantsControllerTests : TestBase
{
    private readonly AdminTenantsController _c = new(null!);

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M00.F01.I01")]
    public async Task List_returnsFixture()
    {
        var res = await _c.TenantsGet(page: null, pageSize: null);
        Assert.NotEmpty(res.Items);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M00.F01.I02")]
    public async Task Create_addsNewTenant()
    {
        var before = (await _c.TenantsGet(null, null)).Items.Count;
        var t = await _c.TenantsPost(new() { Code = "test-1", Name = "Test 1" });
        Assert.NotEqual(Guid.Empty, t.Id);
        Assert.Equal("test-1", t.Code);
        var after = (await _c.TenantsGet(null, null)).Items.Count;
        Assert.Equal(before + 1, after);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M00.F01.I03")]
    public async Task GetById_returnsTenant()
    {
        var t = await _c.TenantsGet(InMemoryStore.AcmeId.ToString());
        Assert.Equal("acme", t.Code);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M00.F01.I04")]
    public async Task Patch_updatesName()
    {
        var id = InMemoryStore.AcmeId.ToString();
        var t = await _c.TenantsPatch(id, new() { Name = "ACME Updated" });
        Assert.Equal("ACME Updated", t.Name);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M00.F01.I05")]
    public async Task Delete_removesTenant()
    {
        var before = (await _c.TenantsGet(null, null)).Items.Count;
        await _c.TenantsDelete(InMemoryStore.AcmeId.ToString());
        var after = (await _c.TenantsGet(null, null)).Items.Count;
        Assert.Equal(before - 1, after);
    }
}