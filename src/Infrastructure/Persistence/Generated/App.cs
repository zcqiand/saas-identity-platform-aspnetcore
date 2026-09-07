using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.src.Infrastructure.Persistence.Generated;

public partial class App
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? Icon { get; set; }

    public int SortOrder { get; set; }

    public string ClientId { get; set; } = null!;

    public string? ClientSecretHash { get; set; }

    public List<string> RedirectUris { get; set; } = null!;

    public List<string> Scopes { get; set; } = null!;

    public bool IsFirstParty { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Menu> Menus { get; set; } = new List<Menu>();

    public virtual ICollection<OauthCode> OauthCodes { get; set; } = new List<OauthCode>();
}
