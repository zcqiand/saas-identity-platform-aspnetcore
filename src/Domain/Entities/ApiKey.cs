using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>V004 — tenant-scoped API key（TypeSpec ApiKey）。secret_hash 不可逆散列。</summary>
public class ApiKey
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string SecretHash { get; set; } = "";
    public string Status { get; set; } = "active";  // PG native enum api_key_status
    public List<string> Scopes { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}