using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;

public partial class OauthClient
{
    public Guid Id { get; set; }

    public string ClientId { get; set; } = null!;

    public string ClientSecret { get; set; } = null!;

    public string ClientName { get; set; } = null!;

    public string GrantTypes { get; set; } = null!;

    public string RedirectUris { get; set; } = null!;

    public string? Scopes { get; set; }

    public int AccessTokenValidity { get; set; }

    public int RefreshTokenValidity { get; set; }

    public bool AutoApprove { get; set; }

    public short Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<OauthAccessToken> OauthAccessTokens { get; set; } = new List<OauthAccessToken>();

    public virtual ICollection<OauthCode> OauthCodes { get; set; } = new List<OauthCode>();

    public virtual ICollection<OauthRefreshToken> OauthRefreshTokens { get; set; } = new List<OauthRefreshToken>();

    public virtual ICollection<SysMenu> SysMenus { get; set; } = new List<SysMenu>();

    public virtual ICollection<SysRole> SysRoles { get; set; } = new List<SysRole>();

    public virtual ICollection<TenantApplication> TenantApplications { get; set; } = new List<TenantApplication>();
}
