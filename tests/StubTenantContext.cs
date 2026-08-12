using Saas.Identity.AspNetCore.Security;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// 测试用 TenantContext 子类：覆写 CurrentTenantId() 返回固定值。
/// 让 TenantGuard.VerifyPathTenant 在无 HttpContext 的单测场景通过。
///
/// 用法：var guard = new TenantGuard(new StubTenantContext { TenantId = "abc" });
/// </summary>
public class StubTenantContext : TenantContext
{
    public string TenantId { get; set; } = "";

    public StubTenantContext() : base(null) { }

    public override string? CurrentTenantId() => string.IsNullOrEmpty(TenantId) ? null : TenantId;
}