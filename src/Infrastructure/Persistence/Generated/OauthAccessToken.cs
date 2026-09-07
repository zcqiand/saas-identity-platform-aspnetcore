using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;

public partial class OauthAccessToken
{
    public Guid Id { get; set; }

    public string TokenId { get; set; } = null!;

    public string AccessToken { get; set; } = null!;

    public string ClientId { get; set; } = null!;

    public Guid? UserId { get; set; }

    public Guid? TenantId { get; set; }

    public string? Scope { get; set; }

    public string TokenType { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public bool Revoked { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual OauthClient Client { get; set; } = null!;

    public virtual ICollection<OauthRefreshToken> OauthRefreshTokens { get; set; } = new List<OauthRefreshToken>();

    public virtual Tenant? Tenant { get; set; }

    public virtual SysUser? User { get; set; }
}
