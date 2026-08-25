using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Saas.Identity.AspNetCore.Security;

/// <summary>
/// JwtIssuer — HS256 签名 JWT（Phase 6 OAuth + Phase 5 password login 共用）。
///
/// 从 AuthController.IssueAccessToken 抽出（v0.4.0 之前 v0.1.10 加 HS256 时塞在
/// AuthController 内, 现在 OauthController 也要用, 提到独立 service）。
///
/// Claims:
///   sub (userId) — JWT 标准 sub claim（mapInboundClaims=false 让名字保留）
///   tenant_id     — saas 域 tenant claim（lab-aspnetcore JwtBearer 用同一 key 校验）
///   jti           — 防重放随机 id
///   iat / nbf / exp — 标准时间字段
///
/// 配置:
///   Jwt:SigningKey ≥32B (HS256), Jwt:Issuer, Jwt:Audience。
///   3 个 saas 后端（nextjs / aspnetcore / springboot）+ 3 个 lab 后端的 JWT 签名 key
///   单一共享 —— 见 stateful-cuddling-cherny.md 决策 §1。
/// </summary>
public class JwtIssuer
{
    private readonly string _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _ttlSeconds;

    public JwtIssuer(IConfiguration config)
    {
        _signingKey = config["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey not configured");
        _issuer = config["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer not configured");
        _audience = config["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience not configured");

        // HS256 ≥32 字节（256 bits），不足直接抛 — 防止 prod 用弱 dev 默认 key
        if (Encoding.UTF8.GetByteCount(_signingKey) < 32)
            throw new InvalidOperationException($"Jwt:SigningKey must be >=32 bytes for HS256 (got {Encoding.UTF8.GetByteCount(_signingKey)})");

        _ttlSeconds = int.TryParse(config["Jwt:TtlSeconds"], out var t) ? t : 3600;
    }

    /// <summary>
    /// 发 access token。claims: sub (userId), tenant_id, jti。
    /// </summary>
    public string IssueAccessToken(Guid userId, Guid tenantId, int? ttlSeconds = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var ttl = ttlSeconds ?? _ttlSeconds;

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("tenant_id", tenantId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddSeconds(ttl),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// 生成 refresh token。格式 saas-rt-{userId}-{ts}-{rand} —— 与 saas-nextjs
    /// lib/oauth-store.ts:97-99 同款, 方便 lab 仓与 saas-nextjs 共享 audit log 排障。
    /// 实际不解析格式（仅 opaque string）, 存 oauth_codes 表的 code 列。
    /// </summary>
    public static string GenerateRefreshToken(Guid userId)
    {
        var rand = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("=", "").Replace("+", "-").Replace("/", "_");
        return $"saas-rt-{userId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{rand}";
    }
}