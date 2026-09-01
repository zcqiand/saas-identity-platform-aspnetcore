using System;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>V006 — 一租户一行；决定 audit_events 自动清理窗口（M06.F02）。</summary>
public class AuditRetentionPolicy
{
    public Guid TenantId { get; set; }
    public int RetentionDays { get; set; } = 90;
    public DateTimeOffset UpdatedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
}