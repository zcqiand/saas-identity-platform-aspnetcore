using System;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>V005 — 树形菜单（parent_id 自引用）。</summary>
public class Menu
{
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string? Icon { get; set; }
    public string Type { get; set; } = "page";  // PG native enum menu_type
    public int SortOrder { get; set; } = 0;
    public string Status { get; set; } = "active";  // PG native enum menu_status
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}