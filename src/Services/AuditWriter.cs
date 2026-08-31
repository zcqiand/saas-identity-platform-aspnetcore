using System.Security.Claims;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;

namespace Saas.Identity.AspNetCore.Services;

/// <summary>
/// M06.F03.I01 审计写入助手 —— 写端点副作用。所有 insert 共用同一形状：
/// { tenantId, actorUserId(从 sub claim), action, targetUserId, metadata={...} }。
/// 不预置 id：EF Core @GeneratedValue 在 SaveChanges 时生成。
/// 失败不抛：审计是 best-effort，写失败不能阻断主业务（参照 msw handlers-extra
/// writeAudit 的 best-effort 语义）。日志由 host logger 兜底。
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
    private readonly AppDbContext _db;
    private readonly ILogger<AuditWriter> _log;

    public AuditWriter(AppDbContext db, ILogger<AuditWriter> log)
    {
        _db = db;
        _log = log;
    }

    public async Task WriteAsync(
      string tenantId,
      string? actorUserId,
      string action,
      string? targetUserId,
      IDictionary<string, object?> metadata,
      CancellationToken ct = default)
    {
        try
        {
            var entry = new AuditEvent
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.Parse(tenantId),
                ActorUserId = actorUserId is null ? null : Guid.Parse(actorUserId),
                Action = action,
                TargetUserId = targetUserId is null ? null : Guid.Parse(targetUserId),
                Metadata = new Dictionary<string, object?>(metadata),
                OccurredAt = DateTimeOffset.UtcNow,
            };
            _db.AuditEvents.Add(entry);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AuditWriter.WriteAsync failed (action={Action}, tenantId={TenantId})", action, tenantId);
        }
    }
}
