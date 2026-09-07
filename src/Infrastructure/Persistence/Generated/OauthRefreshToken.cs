using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;

public partial class OauthRefreshToken
{
    public Guid Id { get; set; }

    public string RefreshToken { get; set; } = null!;

    public Guid AccessTokenId { get; set; }

    public string ClientId { get; set; } = null!;

    public Guid? UserId { get; set; }

    public Guid? TenantId { get; set; }

    public DateTime ExpiresAt { get; set; }

    public bool Revoked { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual OauthAccessToken AccessToken { get; set; } = null!;

    public virtual OauthClient Client { get; set; } = null!;

    public virtual Tenant? Tenant { get; set; }

    public virtual SysUser? User { get; set; }
}
