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
    public DateTimeOffset CreatedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset UpdatedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
}