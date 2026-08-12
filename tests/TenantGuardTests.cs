using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// M00.F01.I03 — TenantGuard 单元测试：path tenantId vs JWT tenant_id claim。
/// 拦截 / 通行两个 case。
/// </summary>
public class TenantGuardTests
{
    [Fact]
    [Trait("Fn", "M00.F01.I03")]
    public void VerifyPathTenant_throwsOnMismatch()
    {
        var guard = new TenantGuard(new StubTenantContext { TenantId = "tenant-A" });
        Assert.Throws<UnauthorizedAccessException>(() => guard.VerifyPathTenant("tenant-B"));
    }

    [Fact]
    [Trait("Fn", "M00.F01.I03")]
    public void VerifyPathTenant_acceptsMatch()
    {
        var guard = new TenantGuard(new StubTenantContext { TenantId = "tenant-A" });
        guard.VerifyPathTenant("tenant-A"); // should not throw
    }
}