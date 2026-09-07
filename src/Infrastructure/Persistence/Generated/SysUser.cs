using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;

public partial class SysUser
{
    public Guid Id { get; set; }

    public string Username { get; set; } = null!;

    public string Password { get; set; } = null!;

    public string? Email { get; set; }

    public string? Mobile { get; set; }

    public short Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int FailedAttempts { get; set; }

    public DateTime? LockedUntil { get; set; }

    public virtual ICollection<OauthAccessToken> OauthAccessTokens { get; set; } = new List<OauthAccessToken>();

    public virtual ICollection<OauthCode> OauthCodes { get; set; } = new List<OauthCode>();

    public virtual ICollection<OauthRefreshToken> OauthRefreshTokens { get; set; } = new List<OauthRefreshToken>();

    public virtual ICollection<TenantMember> TenantMembers { get; set; } = new List<TenantMember>();
}
