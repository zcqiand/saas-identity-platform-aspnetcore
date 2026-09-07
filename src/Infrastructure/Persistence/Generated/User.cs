using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.src.Infrastructure.Persistence.Generated;

public partial class User
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Username { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? DisplayName { get; set; }

    public string? PasswordHash { get; set; }

    public List<Guid> RoleIds { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<AuditEvent> AuditEventActorUsers { get; set; } = new List<AuditEvent>();

    public virtual ICollection<AuditEvent> AuditEventTargetUsers { get; set; } = new List<AuditEvent>();

    public virtual Tenant Tenant { get; set; } = null!;

    public virtual ICollection<TenantMembership> TenantMemberships { get; set; } = new List<TenantMembership>();
}
