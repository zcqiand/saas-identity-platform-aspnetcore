using Saas.Identity.AspNetCore.Services;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// 测试用 no-op IAuditWriter：AuthController 构造器 2026-09-02 起 requires IAuditWriter，
/// 不关心审计断言的旧测试用它占位（审计形状断言在 AuditSideEffectTests 用 Moq verify）。
/// </summary>
public class NoopAuditWriter : IAuditWriter
{
    public Task WriteAsync(
        string tenantId,
        string? actorUserId,
        string action,
        string? targetUserId,
        IDictionary<string, object?> metadata,
        CancellationToken ct = default) => Task.CompletedTask;
}
