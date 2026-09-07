using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.src.Infrastructure.Persistence.Generated;

public partial class Tenant
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Settings { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();

    public virtual ICollection<AuditEvent> AuditEvents { get; set; } = new List<AuditEvent>();

    public virtual AuditRetentionPolicy? AuditRetentionPolicy { get; set; }

    public virtual ICollection<RoleMenuGrant> RoleMenuGrants { get; set; } = new List<RoleMenuGrant>();

    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();

    public virtual ICollection<TenantMembership> TenantMemberships { get; set; } = new List<TenantMembership>();

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
