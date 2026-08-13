using System;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>V003 — 平台级 permission 字典。</summary>
public class Permission
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}