using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using DbAudit = Saas.Identity.AspNetCore.Domain.Entities.AuditEvent;
using DbRetention = Saas.Identity.AspNetCore.Domain.Entities.AuditRetentionPolicy;
using Saas.Identity.AspNetCore.Security;
// alias 避免与 NSwag-generated DTO `AuditEvent` 冲突
using ApiAuditEvent = Saas.Identity.AspNetCore.Controllers.Generated.AuditEvent;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M06.F01 审计事件查询 + M06.F02 留存策略（tenant-scoped）。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// </summary>
public class TenantAuditController : TenantAuditControllerBase
{
    private readonly TenantGuard _guard;
    private readonly AppDbContext _db;

    public TenantAuditController(TenantGuard guard, AppDbContext db)
    {
        _guard = guard;
        _db = db;
    }

    // === DTO 转换 ===

    private static ApiAuditEvent ToEventDto(DbAudit e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        ActorUserId = e.ActorUserId ?? Guid.Empty,
        Action = ToActionDto(e.Action),
        TargetUserId = e.TargetUserId ?? Guid.Empty,
        Metadata = e.Metadata,
        OccurredAt = e.OccurredAt,
    };

    private static AuditAction ToActionDto(string s) => Enum.TryParse<AuditAction>(s, true, out var a)
        ? a : AuditAction.User_created;

    private static string ToActionDb(AuditAction a) => a.ToString().ToLowerInvariant().Replace("_", "_");

    // === endpoints ===

    public override async Task<Response5> AuditEvents(string tenantId, int? page, int? pageSize, string actorUserId, AuditAction? action, DateTimeOffset? from, DateTimeOffset? to)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        var q = _db.AuditEvents.Where(e => e.TenantId == tid);
        if (!string.IsNullOrEmpty(actorUserId)) q = q.Where(e => e.ActorUserId == Guid.Parse(actorUserId));
        if (action.HasValue) q = q.Where(e => e.Action == ToActionDb(action.Value));
        if (from.HasValue) q = q.Where(e => e.OccurredAt >= from.Value);
        if (to.HasValue) q = q.Where(e => e.OccurredAt <= to.Value);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(e => e.OccurredAt)
            .Skip(p * ps).Take(ps).ToListAsync();
        return new Response5
        {
            Items = items.Select(ToEventDto).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<Response6> ByUser(string tenantId, string userId, int? page, int? pageSize)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var uid = Guid.Parse(userId);
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        var q = _db.AuditEvents.Where(e => e.TenantId == tid && e.ActorUserId == uid);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(e => e.OccurredAt)
            .Skip(p * ps).Take(ps).ToListAsync();
        return new Response6
        {
            Items = items.Select(ToEventDto).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override Task<Response7> Export(string tenantId, Body3 body)
    {
        // M06.F01.I03 导出（CSV/JSON URL）— Phase 5 占位
        _guard.VerifyPathTenant(tenantId);
        return Task.FromResult(new Response7
        {
            DownloadUrl = $"https://example.com/audit-export-{tenantId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{body.Format.ToString().ToLowerInvariant()}",
        });
    }

    public override async Task<Response8> RetentionGet(string tenantId)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var row = await _db.AuditRetentionPolicies.FirstOrDefaultAsync(r => r.TenantId == tid);
        return new Response8 { RetentionDays = row?.RetentionDays ?? 90 };
    }

    public override async Task<Response9> RetentionPut(string tenantId, Body4 body)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var existing = await _db.AuditRetentionPolicies.FirstOrDefaultAsync(r => r.TenantId == tid);
        if (existing == null)
        {
            _db.AuditRetentionPolicies.Add(new DbRetention
            {
                TenantId = tid,
                RetentionDays = body.RetentionDays,
            });
        }
        else
        {
            existing.RetentionDays = body.RetentionDays;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync();
        return new Response9 { RetentionDays = body.RetentionDays };
    }
}