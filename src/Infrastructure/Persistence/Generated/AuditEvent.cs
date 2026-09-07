using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.src.Infrastructure.Persistence.Generated;

public partial class AuditEvent
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid? ActorUserId { get; set; }

    public Guid? TargetUserId { get; set; }

    public string Metadata { get; set; } = null!;

    public DateTime OccurredAt { get; set; }

    public virtual User? ActorUser { get; set; }

    public virtual User? TargetUser { get; set; }

    public virtual Tenant Tenant { get; set; } = null!;
}
