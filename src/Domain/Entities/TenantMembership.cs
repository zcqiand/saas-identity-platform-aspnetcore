using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>V002 — cross-tenant 成员关系（TypeSpec TenantMembership）。</summary>
public class TenantMembership
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
    public string Status { get; set; } = "invited";  // PG native enum membership_status
    public DateTimeOffset JoinedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
}