using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// M00.F01.I03 — TenantGuard 单元测试：path tenantId vs JWT tenant_id claim。
/// 拦截 / 通行 / dev 也无兜底三个 case（ADR-0019 审计红线 #3）。
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

    [Fact]
    [Trait("Fn", "M00.F01.I03")]
    public void VerifyPathTenant_throwsWhenJwtMissing_evenInDev()
    {
        // ADR-0019：业务身份字段缺失必须 throw，dev 环境也不例外。
        // 构造已无 IHostEnvironment 参数（dev 兜底分支已删），此测试锁定：
        // 即使整个进程处于 Development，JWT 缺 tenant_id 也必须 401。
        var guard = new TenantGuard(new StubTenantContext { TenantId = null });
        Assert.Throws<UnauthorizedAccessException>(() => guard.VerifyPathTenant("tenant-A"));
    }
}
