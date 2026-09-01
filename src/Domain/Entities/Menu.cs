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
    // 2026-09-01 contract-test I51：属性换 PG enum 类型（string 参数化成 text → 写 42804）
    public MenuTypePg Type { get; set; } = MenuTypePg.page;
    public int SortOrder { get; set; } = 0;
    public MenuStatusPg Status { get; set; } = MenuStatusPg.active;
    public DateTimeOffset CreatedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset UpdatedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
}