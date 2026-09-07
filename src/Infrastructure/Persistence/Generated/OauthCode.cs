using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;

public partial class OauthCode
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string ClientId { get; set; } = null!;

    public Guid UserId { get; set; }

    public Guid TenantId { get; set; }

    public string? RedirectUri { get; set; }

    public string? Scope { get; set; }

    public string? CodeChallenge { get; set; }

    public string? CodeChallengeMethod { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual OauthClient Client { get; set; } = null!;

    public virtual Tenant Tenant { get; set; } = null!;

    public virtual SysUser User { get; set; } = null!;
}
