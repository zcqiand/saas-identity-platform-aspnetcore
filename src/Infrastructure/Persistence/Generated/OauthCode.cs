using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.src.Infrastructure.Persistence.Generated;

public partial class OauthCode
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string GrantType { get; set; } = null!;

    public Guid AppId { get; set; }

    public Guid? UserId { get; set; }

    public Guid TenantId { get; set; }

    public string? RedirectUri { get; set; }

    public string? Scope { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual App App { get; set; } = null!;
}
