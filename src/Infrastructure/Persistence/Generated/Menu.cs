using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.src.Infrastructure.Persistence.Generated;

public partial class Menu
{
    public Guid Id { get; set; }

    public Guid AppId { get; set; }

    public Guid? ParentId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Path { get; set; }

    public string? Icon { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual App App { get; set; } = null!;

    public virtual ICollection<Menu> InverseParent { get; set; } = new List<Menu>();

    public virtual Menu? Parent { get; set; }
}
