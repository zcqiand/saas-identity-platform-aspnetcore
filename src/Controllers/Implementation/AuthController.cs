using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using DbUser = Saas.Identity.AspNetCore.Domain.Entities.User;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M03.F01 密码登录 + M03.F02 OIDC + M03.F03 登出。
/// 公开端点（不需要 TenantGuard）。v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// v0.1.10：AccessToken 改 HS256 签名（之前 v0.4.0 用 alg=none 仅 dev 路径接受，
///          生产 JwtBearer 默认拒收 → 401/500。HS256 走真实对称密钥，
///          Program.cs JwtBearer 用同一 key 校验, dev/prod 同路径）。
/// v0.2.0：HS256 签名抽到 Security/JwtIssuer.cs（OauthController 也用）。
/// v0.3.10 (PLAN-2026-001 T-3b)：M03.F01.I01 saas session cookie + lockout。
/// </summary>
public class AuthController : AuthControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtIssuer _jwt;
    private readonly SaasSessionStore _sessions;
    private readonly FailedLoginStore _failedLogins;

    public AuthController(
        AppDbContext db,
        JwtIssuer jwt,
        SaasSessionStore sessions,
        FailedLoginStore failedLogins)
    {
        _db = db;
        _jwt = jwt;
        _sessions = sessions;
        _failedLogins = failedLogins;
    }

    public override async Task<LoginResponse> Login(LoginRequest body)
    {
        // M03.F01.I02 锁定检查（先查 — 即使密码对也不让锁定中账号登录）
        var username = body.Username ?? "";
        _failedLogins.EnsureNotLocked(username);

        // M03.F01.I01 账号密码登录
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null || string.IsNullOrEmpty(body.Password))
        {
            _failedLogins.RecordFailure(username);
            throw new UnauthorizedAccessException("invalid credentials");
        }

        // Phase 5：dev seed password_hash 写成 "plain:{password}"；真实换 argon2
        var ok = user.PasswordHash == $"plain:{body.Password}" || user.PasswordHash == body.Password;
        if (!ok)
        {
            _failedLogins.RecordFailure(username);
            throw new UnauthorizedAccessException("invalid credentials");
        }
        if (user.Status == "suspended" || user.Status == "disabled")
            throw new UnauthorizedAccessException("user disabled");

        // 成功 — 清失败计数 + 写 saas session cookie
        _failedLogins.ResetSuccess(user.Username);

        var sid = _sessions.GenerateId();
        var session = new SaasSession(
            UserId: user.Id,
            TenantId: user.TenantId,
            CreatedAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.Add(_sessions.DefaultTtl));
        _sessions.Put(session with { Id = sid });

        // 写 Set-Cookie 头（HttpOnly + SameSite=Lax；Secure 由反向代理加）
        Response.Cookies.Append(
            SaasSessionMiddleware.CookieName,
            sid,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Path = "/api",
                Expires = session.ExpiresAt,
            });

        return new LoginResponse
        {
            AccessToken = _jwt.IssueAccessToken(user.Id, user.TenantId),
            RefreshToken = $"refresh-{user.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            TokenType = "Bearer",
            ExpiresIn = 3600,
            UserId = user.Id,
            CurrentTenantId = user.TenantId,
        };
    }

    public override Task Logout()
    {
        // M03.F03.I05/I06 登出（无状态 JWT 仅前端清 cookie）
        return Task.CompletedTask;
    }

    public override Task<TokenResponse> Callback(OidcCallbackRequest body)
    {
        // M03.F02.I03 OIDC Code 换取（Phase 5 占位：直接返回 mock）
        return Task.FromResult(new TokenResponse
        {
            AccessToken = "oidc-access-token",
            TokenType = "Bearer",
            ExpiresIn = 3600,
            Scope = "",
        });
    }

    public override Task<TokenResponse> Refresh(TokenRequest body)
    {
        // M03.F02.I04 refresh token（Phase 5 占位：解析格式 + 重发）
        var match = Refresh格式(body.RefreshToken);
        var userId = match?.userId ?? Guid.Empty;
        var tenantId = match?.tenantId ?? Guid.Empty;
        return Task.FromResult(new TokenResponse
        {
            AccessToken = _jwt.IssueAccessToken(userId, tenantId),
            RefreshToken = $"refresh-{userId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            TokenType = "Bearer",
            ExpiresIn = 3600,
            Scope = "",
        });
    }

    private static (Guid userId, Guid tenantId)? Refresh格式(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var parts = token.Split('-');
        if (parts.Length < 3 || parts[0] != "refresh") return null;
        if (!Guid.TryParse(parts[1], out var u)) return null;
        return (u, Guid.Empty);  // Phase 5 占位
    }
}