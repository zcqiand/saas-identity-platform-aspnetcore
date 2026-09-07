using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;

public partial class SysMenu
{
    public Guid Id { get; set; }

    public string ClientId { get; set; } = null!;

    public Guid ParentId { get; set; }

    public string Title { get; set; } = null!;

    public short Type { get; set; }

    public string? Path { get; set; }

    public string? Component { get; set; }

    public string? Perms { get; set; }

    public string? Icon { get; set; }

    public int SortOrder { get; set; }

    public short Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual OauthClient Client { get; set; } = null!;

    public virtual ICollection<SysRole> Roles { get; set; } = new List<SysRole>();
}
