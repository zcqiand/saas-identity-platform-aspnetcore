using System;
using Microsoft.EntityFrameworkCore;

namespace Saas.Identity.AspNetCore.Domain.Entities;

/// <summary>
/// V014 — OAuth 2.0 authorization_code + refresh_token 存储。
/// 镜像 shared/sql/migrations/V014__seed_lab_mgmt_app.sql 的 oauth_codes 表；
/// 替代 saas-nextjs 进程内 oauth-store（src/lib/oauth-store.ts），让 3 个 saas 后端
/// （nextjs / aspnetcore / springboot）共用同一持久化 schema。
///
/// grant_type 列区分：
///   - "authorization_code" 一次性消费（consumed_at 非 NULL = 已用），TTL 10min
///   - "refresh_token" 旋转换发（每次 /token 旧 refresh 被 consumed，新 refresh 写入），TTL 7d
/// </summary>
public class OauthCode
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string GrantType { get; set; } = "authorization_code";  // authorization_code | refresh_token
    public Guid AppId { get; set; }
    public Guid? UserId { get; set; }     // authorization_code 创建时为 NULL；/token 交换后填入
    public Guid TenantId { get; set; }
    public string? RedirectUri { get; set; }
    public string? Scope { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}