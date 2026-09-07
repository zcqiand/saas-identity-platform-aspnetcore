using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.src.Infrastructure.Persistence.Generated;

public partial class RoleMenuGrant
{
    public Guid RoleId { get; set; }

    public Guid TenantId { get; set; }

    public List<Guid> MenuIds { get; set; } = null!;

    public DateTime UpdatedAt { get; set; }

    public virtual Role Role { get; set; } = null!;

    public virtual Tenant Tenant { get; set; } = null!;
}
