using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;

public partial class TenantApplication
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string ClientId { get; set; } = null!;

    public short Status { get; set; }

    public DateTime? ExpireTime { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual OauthClient Client { get; set; } = null!;

    public virtual Tenant Tenant { get; set; } = null!;
}
