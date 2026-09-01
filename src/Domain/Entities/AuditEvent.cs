using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>V006 — tenant-scoped 不可变审计事件（应用层 insert-only）。</summary>
public class AuditEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = "";  // PG native enum audit_action
    public Guid? TargetUserId { get; set; }
    public Dictionary<string, object?> Metadata { get; set; } = new();
    public DateTimeOffset OccurredAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
}