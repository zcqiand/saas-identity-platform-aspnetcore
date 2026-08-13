using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M06.F01 审计事件查询 + M06.F02 留存策略（tenant-scoped）。
/// </summary>
public class TenantAuditControllerTests : TestBase
{
    private TenantAuditController NewC()
    {
        var ctx = new StubTenantContext { TenantId = InMemoryStore.AcmeId.ToString() };
        return new TenantAuditController(new TenantGuard(ctx), null!);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M06.F01.I01")]
    public async Task List_returnsAuditEvents()
    {
        var c = NewC();
        var res = await c.AuditEvents(InMemoryStore.AcmeId.ToString(), null, null, null, null, null, null);
        Assert.NotEmpty(res.Items);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M06.F01.I02")]
    public async Task ByUser_returnsEventsForUser()
    {
        var c = NewC();
        var res = await c.ByUser(InMemoryStore.AcmeId.ToString(), InMemoryStore.AliceId.ToString(), null, null);
        Assert.NotEmpty(res.Items);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M06.F01.I03")]
    public async Task Export_returnsDownloadUrl()
    {
        var c = NewC();
        var res = await c.Export(InMemoryStore.AcmeId.ToString(), new() { From = DateTimeOffset.UtcNow.AddDays(-7), To = DateTimeOffset.UtcNow });
        Assert.False(string.IsNullOrEmpty(res.DownloadUrl));
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M06.F02.I04")]
    public async Task Retention_getPut_roundtrip()
    {
        var c = NewC();
        var get = await c.RetentionGet(InMemoryStore.AcmeId.ToString());
        Assert.True(get.RetentionDays > 0);
        var put = await c.RetentionPut(InMemoryStore.AcmeId.ToString(), new() { RetentionDays = 180 });
        Assert.Equal(180, put.RetentionDays);
    }
}