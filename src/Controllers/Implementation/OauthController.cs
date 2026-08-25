using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using AppEntity = Saas.Identity.AspNetCore.Domain.Entities.App;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M04 OAuth 授权码签发 + 令牌交换 / 刷新。
/// v0.4.0：Phase 5 mock（Guid.NewGuid() + 字面量字符串）。
/// v0.2.0：Phase 6 真 OAuth —
///   - apps.client_id 校验（apps 表 App 实体，V014 seed lab-mgmt）
///   - apps.redirect_uris 包含请求 redirect_uri
///   - apps.scopes 包含请求 scope（V014 scopes = lab.read, lab.write）
///   - oauth_codes 表存 authorization_code (TTL 10min) + refresh_token (TTL 7d)
///   - JwtIssuer.IssueAccessToken 签 HS256 access token（同 AuthController 共用）
///   - 与 saas-nextjs/app/api/v1/oauth/{authorize,token}/route.ts 语义一致：
///     grantType=authorization_code 验 redirect_uri 一致; grantType=refresh_token 旋转 (旧 consumed, 新写入)。
///
/// 错误码（对应 saas-nextjs OAuthError codes）：
///   INVALID_CLIENT         apps.client_id 不存在 → UnauthorizedAccessException (401)
///   INVALID_REDIRECT_URI   apps.redirect_uris 不含请求 redirect_uri → ArgumentException (400)
///   INVALID_SCOPE          apps.scopes 不包含请求 scope → ArgumentException (400)
///   INVALID_GRANT          code 不存在 / 已消费 / 过期 / redirect_uri 不一致 → UnauthorizedAccessException (401)
///   INVALID_REQUEST        缺必填字段 → ArgumentException (400)
/// </summary>
public class OauthController : OauthControllerBase
{
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RefreshTtl = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;
    private readonly JwtIssuer _jwt;

    public OauthController(AppDbContext db, JwtIssuer jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    // M04.F03.I07 — 授权码签发
    public override async Task<Response3> Authorize(AuthorizeCodeRequest body)
    {
        // 1. clientId 必须是已注册 OAuth client（apps.client_id；Guid → string 后查）
        var clientIdStr = body.ClientId.ToString();
        var app = await _db.Apps.FirstOrDefaultAsync(a => a.ClientId == clientIdStr);
        if (app == null || app.Status != "active")
            throw new UnauthorizedAccessException($"INVALID_CLIENT: clientId={clientIdStr} not registered");

        // 2. redirectUri 必须在 apps.redirect_uris 白名单里。RFC 6749 §3.1.2 允许 query
        //    参数差异（lab 前端回跳带 ?from=<业务路径>），匹配规则 = 白名单条目是
        //    请求 redirectUri 的前缀且边界在 '?' 处。
        if (!app.RedirectUris.Any(u => body.RedirectUri == u
                || (body.RedirectUri.StartsWith(u, StringComparison.Ordinal)
                    && body.RedirectUri[u.Length] == '?')))
            throw new ArgumentException($"INVALID_REDIRECT_URI: {body.RedirectUri} not in app.redirect_uris");

        // 3. scope：RFC 6749 §3.3 space-separated 列表，请求的每个 scope 都必须 ∈ apps.scopes
        //    （子集校验）。曾用整串 Contains 精确匹配，lab 发 "lab.read lab.write" 被拒。
        var requestedScopes = (body.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestedScopes.Length == 0 || requestedScopes.Any(s => !app.Scopes.Contains(s)))
            throw new ArgumentException($"INVALID_SCOPE: scope '{body.Scope}' not a subset of app.scopes");

        // 4. 生成 code 格式: saas-code-{ts-ms}-{rand-base64}（与 saas-nextjs 同款便于跨 IdP 排障）
        var code = GenerateCode();

        var oauthCode = new OauthCode
        {
            Code = code,
            GrantType = "authorization_code",
            AppId = app.Id,
            TenantId = body.TenantId,
            RedirectUri = body.RedirectUri,
            Scope = body.Scope,
            ExpiresAt = DateTimeOffset.UtcNow.Add(CodeTtl),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.OauthCodes.Add(oauthCode);
        await _db.SaveChangesAsync();

        return new Response3
        {
            Code = code,
            State = body.State,
        };
    }

    // M04.F03.I08 + I09 — 令牌交换 + 刷新（按 grantType 路由）
    public override async Task<TokenResponse> Token(TokenRequest body)
    {
        var clientIdStr = body.ClientId.ToString();
        var app = await _db.Apps.FirstOrDefaultAsync(a => a.ClientId == clientIdStr);
        if (app == null || app.Status != "active")
            throw new UnauthorizedAccessException($"INVALID_CLIENT: clientId={clientIdStr} not registered");

        // dev 暂不验 clientSecret（saas-nextjs 同模式; prod Phase 6+ 加 Argon2 hash 校验）
        // 生产路径：if (!BCrypt.Verify(body.ClientSecret ?? "", app.ClientSecretHash ?? "")) throw ...

        return body.GrantType switch
        {
            TokenRequestGrantType.Authorization_code => await ExchangeAuthorizationCode(app, body),
            TokenRequestGrantType.Refresh_token => await RotateRefreshToken(app, body),
            _ => throw new ArgumentException("UNSUPPORTED_GRANT_TYPE"),
        };
    }

    private async Task<TokenResponse> ExchangeAuthorizationCode(AppEntity app, TokenRequest body)
    {
        if (string.IsNullOrEmpty(body.Code))
            throw new ArgumentException("INVALID_REQUEST: code required for grantType=authorization_code");
        if (string.IsNullOrEmpty(body.RedirectUri))
            throw new ArgumentException("INVALID_REQUEST: redirectUri required for grantType=authorization_code");

        // 一次性消费：查 code 必须未消费且未过期
        var oauthCode = await _db.OauthCodes.FirstOrDefaultAsync(c =>
            c.Code == body.Code && c.GrantType == "authorization_code");
        if (oauthCode == null)
            throw new UnauthorizedAccessException("INVALID_GRANT: code not found");
        if (oauthCode.ConsumedAt != null)
            throw new UnauthorizedAccessException("INVALID_GRANT: code already consumed");
        if (oauthCode.ExpiresAt < DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("INVALID_GRANT: code expired");
        if (oauthCode.AppId != app.Id)
            throw new UnauthorizedAccessException("INVALID_GRANT: code does not belong to this client");
        if (oauthCode.TenantId != body.TenantId)
            throw new UnauthorizedAccessException("INVALID_GRANT: tenantId mismatch");
        if (oauthCode.RedirectUri != body.RedirectUri)
            throw new UnauthorizedAccessException("INVALID_GRANT: redirectUri mismatch");

        // 业务约束: tenantId 下必须有 user（dev mock 限定; prod 走 saas 共享 user 表）
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TenantId == body.TenantId);
        if (user == null)
            throw new ArgumentException("NO_USER: tenantId has no user");

        // 一次性消费 — 标记 consumed + 写 user_id 供 audit 用
        oauthCode.ConsumedAt = DateTimeOffset.UtcNow;
        oauthCode.UserId = user.Id;

        // 发新 refresh token + access token
        var refresh = JwtIssuer.GenerateRefreshToken(user.Id);
        _db.OauthCodes.Add(new OauthCode
        {
            Code = refresh,
            GrantType = "refresh_token",
            AppId = app.Id,
            UserId = user.Id,
            TenantId = body.TenantId,
            Scope = oauthCode.Scope,
            ExpiresAt = DateTimeOffset.UtcNow.Add(RefreshTtl),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync();

        return new TokenResponse
        {
            AccessToken = _jwt.IssueAccessToken(user.Id, body.TenantId),
            RefreshToken = refresh,
            TokenType = "Bearer",
            ExpiresIn = 3600,
            Scope = oauthCode.Scope ?? "",
        };
    }

    private async Task<TokenResponse> RotateRefreshToken(AppEntity app, TokenRequest body)
    {
        if (string.IsNullOrEmpty(body.RefreshToken))
            throw new ArgumentException("INVALID_REQUEST: refreshToken required for grantType=refresh_token");

        var oldRefresh = await _db.OauthCodes.FirstOrDefaultAsync(c =>
            c.Code == body.RefreshToken && c.GrantType == "refresh_token");
        if (oldRefresh == null)
            throw new UnauthorizedAccessException("INVALID_GRANT: refresh_token not found");
        if (oldRefresh.ConsumedAt != null)
            throw new UnauthorizedAccessException("INVALID_GRANT: refresh_token already consumed (rotate-once semantics)");
        if (oldRefresh.ExpiresAt < DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("INVALID_GRANT: refresh_token expired");
        if (oldRefresh.AppId != app.Id)
            throw new UnauthorizedAccessException("INVALID_GRANT: refresh_token does not belong to this client");
        if (oldRefresh.UserId == null)
            throw new UnauthorizedAccessException("INVALID_GRANT: refresh_token has no user_id");
        if (oldRefresh.TenantId != body.TenantId)
            throw new UnauthorizedAccessException("INVALID_GRANT: tenantId mismatch");

        // 旋转: 旧 refresh 标记 consumed, 新 refresh 写入
        oldRefresh.ConsumedAt = DateTimeOffset.UtcNow;
        var newRefresh = JwtIssuer.GenerateRefreshToken(oldRefresh.UserId.Value);
        _db.OauthCodes.Add(new OauthCode
        {
            Code = newRefresh,
            GrantType = "refresh_token",
            AppId = app.Id,
            UserId = oldRefresh.UserId,
            TenantId = oldRefresh.TenantId,
            Scope = oldRefresh.Scope,
            ExpiresAt = DateTimeOffset.UtcNow.Add(RefreshTtl),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync();

        return new TokenResponse
        {
            AccessToken = _jwt.IssueAccessToken(oldRefresh.UserId.Value, oldRefresh.TenantId),
            RefreshToken = newRefresh,
            TokenType = "Bearer",
            ExpiresIn = 3600,
            Scope = oldRefresh.Scope ?? "",
        };
    }

    private static string GenerateCode()
    {
        var rand = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("=", "").Replace("+", "-").Replace("/", "_");
        return $"saas-code-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{rand}";
    }
}