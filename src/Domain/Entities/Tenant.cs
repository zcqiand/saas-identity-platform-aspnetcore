using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>
/// V001__init_tenants.sql — 多租户根 entity（TypeSpec Tenant）。
/// JSONB settings 列：EF Core 8 + Npgsql 原生支持 jsonb 列，存为 Dictionary&lt;string, object?&gt;。
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "active";  // PG native enum tenant_status
    public Dictionary<string, object?> Settings { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset UpdatedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
}