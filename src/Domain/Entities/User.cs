using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>
/// V002__init_users_memberships.sql — tenant-scoped 用户档案（TypeSpec User）。
/// role_ids 是 Phase 5 待删冗余列；当前镜像 shared SQL 以保 1:1 对齐。
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Status { get; set; } = "invited";  // PG native enum user_status
    public string? PasswordHash { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}