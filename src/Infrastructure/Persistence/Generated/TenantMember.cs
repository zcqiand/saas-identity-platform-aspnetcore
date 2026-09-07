using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;

public partial class TenantMember
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string? MemberName { get; set; }

    public bool IsOwner { get; set; }

    public short Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Tenant Tenant { get; set; } = null!;

    public virtual SysUser User { get; set; } = null!;

    public virtual ICollection<SysRole> Roles { get; set; } = new List<SysRole>();
}
