using System;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>V003 — role ↔ permission M:N 关系（复合主键）。</summary>
public class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public DateTimeOffset GrantedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
}