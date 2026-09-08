using System.Security.Claims;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;

namespace Saas.Identity.AspNetCore.Infrastructure.Audit;

/// <summary>
/// M06.F03.I01 审计写入助手 —— 写端点副作用。所有 insert 共用同一形状：
/// { tenantId, actorUserId(从 sub claim), action, targetUserId, metadata={...} }。
/// 失败不抛：审计是 best-effort，写失败不能阻断主业务。
///
/// v0.5.0：M06 已废（audit_events / audit_retention_policies 表 DROP）—— 接口保留
/// 让 callers（AuthController / AdminTenantsController 等）的依赖注入不破，
/// 实现改为 no-op + 日志。后续如果 M06 重启，把 _db.AuditEvents.Add(...) 加回来。
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(
      string tenantId,
      string? actorUserId,
      string action,
      string? targetUserId,
      IDictionary<string, object?> metadata,
      CancellationToken ct = default);
}

public sealed class AuditWriter : IAuditWriter
{
    private readonly ILogger<AuditWriter> _log;

    public AuditWriter(ILogger<AuditWriter> log)
    {
        _log = log;
    }

    public Task WriteAsync(
      string tenantId,
      string? actorUserId,
      string action,
      string? targetUserId,
      IDictionary<string, object?> metadata,
      CancellationToken ct = default)
    {
        // M06 已废：audit_events 表 DROP。best-effort 写日志即可，不阻断主业务。
        _log.LogInformation(
            "[audit:noop] tenantId={TenantId} actor={Actor} action={Action} target={Target} metadata={Metadata}",
            tenantId, actorUserId ?? "-", action, targetUserId ?? "-", metadata);
        return Task.CompletedTask;
    }
}