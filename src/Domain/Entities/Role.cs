using System;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>V003 — tenant-scoped role（TypeSpec Role）。</summary>
public class Role
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}