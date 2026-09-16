using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
// alias to disambiguate from NSwag-generated DTO User
using DbUser = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysUser;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
// disambiguate DTO vs entity
using DtoSysUser = Saas.Identity.AspNetCore.Controllers.Generated.SysUser;
using DtoMembership = Saas.Identity.AspNetCore.Controllers.Generated.TenantMembership;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Infrastructure.Audit;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M03.F01 密码登录 + M03.F02 OIDC + M03.F03 登出。
/// 公开端点（不需要 TenantGuard）。v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// v0.1.10：AccessToken 改 HS256 签名（之前 v0.4.0 用 alg=none 仅 dev 路径接受，
///          生产 JwtBearer 默认拒收 → 401/500。HS256 走真实对称密钥，
///          Program.cs JwtBearer 用同一 key 校验, dev/prod 同路径）。
/// v0.2.0：HS256 签名抽到 Security/JwtIssuer.cs（OauthController 也用）。
/// v0.3.10 (PLAN-2026-001 T-3b)：M01.F04.I03 saas session cookie + lockout。
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
        // M01.F04.I02 锁定检查（先查 — 即使密码对也不让锁定中账号登录）
        var username = body.Username ?? "";
        _failedLogins.EnsureNotLocked(username);

        // M01.F04.I03 账号密码登录
        var user = await _db.SysUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null || string.IsNullOrEmpty(body.Password))
        {
            _failedLogins.RecordFailure(username);
            throw new UnauthorizedAccessException("invalid credentials");
        }

        // Phase 5：dev seed password 写成 "plain:{password}"；真实换 argon2
        var ok = user.Password == $"plain:{body.Password}" || user.Password == body.Password;
        if (!ok)
        {
            _failedLogins.RecordFailure(username);
            throw new UnauthorizedAccessException("invalid credentials");
        }
        // SysUser.Status 是 short（1=active，0/2=disabled/suspended）—— shared schema 演进
        // 9/7 重组后 status 列从 enum 改为 smallint，具体语义由 shared/src/db/seed.ts 定。
        if (user.Status != 1)
            throw new UnauthorizedAccessException("user disabled");

        // 成功 — 清失败计数 + 写 saas session cookie
        _failedLogins.ResetSuccess(user.Username);

        // sys_user 是 global 自然人，TenantId 在 tenant_member 上（M01.F04 9/7 重组）；
        // 选第一个 active membership 作为 currentTenantId。
        // ADR-0032：availableTenants 是真值 TenantMembership[] —— tenant_application ⨝
        // tenant_member（client_id 匹配、member status=1 active），roleIds 由 MembershipViews
        // 走 tenant_member_role ⨝ sys_role。clientId 未传或该 app 无订阅时退化为全部 active
        // membership（msw oracle：handlers-extra login 只按 userId+active 过滤）。
        var membersQuery = _db.TenantMembers
            .Include(tm => tm.Roles)
            .Where(tm => tm.UserId == user.Id && tm.Status == 1);
        if (!string.IsNullOrEmpty(body.ClientId))
        {
            var appTenantIds = await _db.TenantApplications
                .Where(ta => ta.ClientId == body.ClientId)
                .Select(ta => ta.TenantId)
                .ToListAsync();
            if (appTenantIds.Count > 0)
                membersQuery = membersQuery.Where(tm => appTenantIds.Contains(tm.TenantId));
        }
        var members = await membersQuery.ToListAsync();
        var currentTenantId = members.FirstOrDefault()?.TenantId ?? Guid.Empty;

        // M01.F04.I03 写端点副作用 — login_success（2026-09-02 contract-test M96 audit 覆盖对齐，
        // 形状对齐 nextjs/msw/springboot：actor=target=登录用户，metadata={username}）
        await _audit.WriteAsync(
            currentTenantId.ToString(),
            user.Id.ToString(),
            "login_success",
            targetUserId: null,
            new Dictionary<string, object?> { ["username"] = user.Username });

        var sid = _sessions.GenerateId();
        var session = new SaasSession(
            UserId: user.Id,
            TenantId: currentTenantId,
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
            // ADR-0032 重 emit：LoginResponse = { user: SysUser, availableTenants: TenantMembership[],
            // userId(required), currentTenantId?, accessToken, refreshToken, tokenType, expiresIn, clientId }。
            User = new DtoSysUser
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Mobile = user.Mobile,
                Status = StatusEnumMaps.MapUserStatus(user.Status),
                FailedAttempts = user.FailedAttempts,
                LockedUntil = user.LockedUntil != null ? new DateTimeOffset(DateTime.SpecifyKind(user.LockedUntil.Value, DateTimeKind.Utc)) : default,
                CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc)),
                UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(user.UpdatedAt, DateTimeKind.Utc)),
            },
            AvailableTenants = MembershipViews.FromEntities(members),
            UserId = user.Id,
            CurrentTenantId = currentTenantId,
            // 契约 required string；msw oracle 取请求体 clientId ?? ""（ADR-0019 不适用：
            // login 的 clientId 是路由上下文回显，不是业务身份判权字段）。
            ClientId = body.ClientId ?? "",
            AccessToken = _jwt.IssueAccessToken(user.Id, currentTenantId),
            RefreshToken = $"refresh-{user.Id}-{((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds()}",
            TokenType = "Bearer",
            ExpiresIn = 3600,
        };
    }

    public override Task Logout()
    {
        // M01.F04.I06/I06 登出（无状态 JWT 仅前端清 cookie）
        // 2026-08-31 contract-test M96.F02.I23：无返回 action ASP.NET 默认给 200 空体，
        // 家族契约（msw/springboot/nextjs）logout 是 204 noContent —— 显式对齐。
        Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
    }

    }