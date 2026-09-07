using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.src.Infrastructure.Persistence.Generated;

public partial class AuditRetentionPolicy
{
    public Guid TenantId { get; set; }

    public int RetentionDays { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Tenant Tenant { get; set; } = null!;
}
