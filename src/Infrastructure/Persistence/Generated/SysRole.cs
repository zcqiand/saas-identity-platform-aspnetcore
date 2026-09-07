using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;

public partial class SysRole
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string ClientId { get; set; } = null!;

    public string RoleCode { get; set; } = null!;

    public string RoleName { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsPreset { get; set; }

    public short Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual OauthClient Client { get; set; } = null!;

    public virtual Tenant Tenant { get; set; } = null!;

    public virtual ICollection<TenantMember> Members { get; set; } = new List<TenantMember>();

    public virtual ICollection<SysMenu> Menus { get; set; } = new List<SysMenu>();
}
