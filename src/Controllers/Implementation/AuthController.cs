using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Services;
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
    private readonly IAuditWriter _audit;

    public AuthController(
        AppDbContext db,
        JwtIssuer jwt,
        SaasSessionStore sessions,
        FailedLoginStore failedLogins,
        IAuditWriter audit)
    {
        _db = db;
        _jwt = jwt;
        _sessions = sessions;
        _failedLogins = failedLogins;
        _audit = audit;
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

        // M03.F01.I01 写端点副作用 — login_success（2026-09-02 contract-test M96 audit 覆盖对齐，
        // 形状对齐 nextjs/msw/springboot：actor=target=登录用户，metadata={username}）
        await _audit.WriteAsync(
            user.TenantId.ToString(),
            user.Id.ToString(),
            "login_success",
            targetUserId: null,
            new Dictionary<string, object?> { ["username"] = user.Username });

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
        // 2026-08-31 contract-test M96.F02.I23：无返回 action ASP.NET 默认给 200 空体，
        // 家族契约（msw/springboot/nextjs）logout 是 204 noContent —— 显式对齐。
        Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }

    public override Task<TokenResponse> Callback(OidcCallbackRequest body)
    {
        // M03.F02.I03 OIDC Code 换取。
        // 2026-08-31 contract-test M96.F02.I25：补错误分支 —— 缺 code/state/clientId
        // 原占位实现静默 200，与 msw/nextjs 的 400 分叉。
        if (string.IsNullOrEmpty(body?.Code) || string.IsNullOrEmpty(body.State))
            throw new ArgumentException("OIDC callback: code/state/clientId required");
        // 成功分支需真 IdP code 交换（Phase 6+）；当前 dev 占位签发。
        return Task.FromResult(new TokenResponse
        {
            AccessToken = "oidc-access-token",
            TokenType = "Bearer",
            ExpiresIn = 3600,
            Scope = "",
        });
    }

    public override async Task<TokenResponse> Refresh(TokenRequest body)
    {
        // M03.F02.I04 refresh token。
        // 2026-08-31 contract-test M96.F02.I24 修复：未知/垃圾 token 之前静默重发
        // （Guid.Empty 也签 token）；现在必须验 user 存在才发，否则 401。
        var match = Refresh格式(body?.RefreshToken)
            ?? throw new ArgumentException("invalid refresh_token");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == match.userId);
        if (user is null)
            throw new ArgumentException("invalid refresh_token");
        return new TokenResponse
        {
            AccessToken = _jwt.IssueAccessToken(user.Id, user.TenantId),
            RefreshToken = $"refresh-{user.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            TokenType = "Bearer",
            ExpiresIn = 3600,
            Scope = "",
        };
    }

    private static (Guid userId, Guid tenantId)? Refresh格式(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        // 兼容两种格式：旧 "refresh-<uuid>-<epoch>"（本仓 login 签发）与
        // "saas-rt-<uuid>-<ts>-<rand>"（家族新格式）。UUID 自身含 4 个 '-'，rand(base64url)
        // 也可能含 '-' —— 不能按 lastIndexOf('-') 切（2026-08-31 contract-test M96.F02.I24
        // 修复）：UUID = 前 5 段，后面全是 ts/rand。
        var tokenBody = token.StartsWith("saas-rt-", StringComparison.Ordinal)
            ? token["saas-rt-".Length..]
            : token.StartsWith("refresh-", StringComparison.Ordinal)
                ? token["refresh-".Length..]
                : null;
        if (tokenBody is null) return null;
        var parts = tokenBody.Split('-');
        if (parts.Length < 6) return null; // 5 段 UUID + 至少 1 段尾缀
        return Guid.TryParse(string.Join("-", parts[..5]), out var u) ? (u, Guid.Empty) : null;
    }
}