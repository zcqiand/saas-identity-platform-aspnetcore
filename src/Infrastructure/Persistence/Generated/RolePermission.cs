using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.src.Infrastructure.Persistence.Generated;

public partial class RolePermission
{
    public Guid RoleId { get; set; }

    public Guid PermissionId { get; set; }

    public DateTime GrantedAt { get; set; }

    public virtual Permission Permission { get; set; } = null!;

    public virtual Role Role { get; set; } = null!;
}
