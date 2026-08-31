using Microsoft.AspNetCore.Http;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M05.F01 API Key 生命周期 controller 测试（写端点第二期）。
///
/// 当前 InMemory provider 撞 ApiKey.Scopes 的 PG `text[]` + ApiKeyStatusPg native enum，
/// 无法 mirror 完整 AppDbContext 配置。沿用仓内 M10.F04 约定用 Skip 占位，等 Phase 5
/// Testcontainers PG 落地后再启跑。Controller 实现本身在 src/ 已写完。
///
/// 注意：CLAUDE.md §2「禁止给 skip/xfail 的测试挂功能 ID」——本类 Skip 测试不带
/// [Trait("Fn",...)]，不进 trace.json。等 Phase 5 Testcontainers PG 落地后真启跑
/// 时再补 [Trait]。
/// </summary>
public class TenantApiKeysControllerTests
{
    // === M05.F01 写端点第二期 — 占位测试（Phase 5 Testcontainers PG 启用） ===

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG — ApiKey.Scopes=text[] + ApiKeyStatusPg enum InMemory provider 无法 mirror")]
    public Task ApiKeysPost_createsApiKey_returnsSecret() => Task.CompletedTask;

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    public Task Revoke_marksRevoked_keepsRow() => Task.CompletedTask;

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    public Task ApiKeysDelete_removesRow_returnsCompleted() => Task.CompletedTask;

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    public Task ApiKeysDelete_unknownKey_returns404() => Task.CompletedTask;

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    public Task ApiKeysDelete_crossTenant_throwsUnauthorized() => Task.CompletedTask;
}
