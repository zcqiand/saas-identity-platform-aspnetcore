using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>V005 — tenant-scoped 角色↔菜单 M:N（整批 PUT）。</summary>
public class RoleMenuGrant
{
    public Guid RoleId { get; set; }
    public Guid TenantId { get; set; }
    public List<Guid> MenuIds { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
}