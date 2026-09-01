using System;
using System.Collections.Generic;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>
/// V005 — 平台级统一实体：菜单承载 + OAuth client（TypeSpec App）。
/// grant_types 列在 PG 是 oauth_grant_type[]，在 EF 用 List&lt;string&gt; 映射底层数组。
/// </summary>
public class App
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; } = 0;
    // 2026-09-01 contract-test I45：属性换 PG enum 类型（string 参数化成 text → 写 42804）
    public AppStatusPg Status { get; set; } = AppStatusPg.active;
    public string ClientId { get; set; } = "";
    public string? ClientSecretHash { get; set; }
    public List<string> RedirectUris { get; set; } = new();
    public List<string> Scopes { get; set; } = new();
    public List<OAuthGrantTypePg> GrantTypes { get; set; } = new();  // oauth_grant_type[]
    public bool IsFirstParty { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset UpdatedAt { get; set; } = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
}