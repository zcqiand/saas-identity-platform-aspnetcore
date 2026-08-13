using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using DbUser = Saas.Identity.AspNetCore.Domain.Entities.User;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M03.F01 密码登录 + M03.F02 OIDC + M03.F03 登出。
/// 公开端点（不需要 TenantGuard）。v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// </summary>
public class AuthController : AuthControllerBase
{
    private readonly AppDbContext _db;

    public AuthController(AppDbContext db) { _db = db; }

    private static string B64Url(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .Replace("=", "").Replace("+", "-").Replace("/", "_");

    private static string IssueAccessToken(Guid userId, Guid tenantId) =>
        $"{B64Url("{\"alg\":\"none\"}")}.{B64Url($"{{\"sub\":\"{userId}\",\"tenant_id\":\"{tenantId}\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}")}.dev-placeholder";

    public override async Task<LoginResponse> Login(LoginRequest body)
    {
        // M03.F01.I01 账号密码登录
        var users = await _db.Users
            .Where(u => u.Username == body.Username)
            .Take(1)
            .ToListAsync();
        // 简化为按 username 全表查第一个；生产应加 tenantCode → tenantId 解析 + 在该 tenant 内查
        var user = users.FirstOrDefault() ?? await _db.Users.FirstOrDefaultAsync(u => u.Username == body.Username);
        if (user == null || string.IsNullOrEmpty(body.Password))
            throw new UnauthorizedAccessException("invalid credentials");

        // Phase 5：dev seed password_hash 写成 "plain:{password}"；真实换 argon2
        var ok = user.PasswordHash == $"plain:{body.Password}" || user.PasswordHash == body.Password;
        if (!ok)
            throw new UnauthorizedAccessException("invalid credentials");
        if (user.Status == "suspended" || user.Status == "disabled")
            throw new UnauthorizedAccessException("user disabled");

        return new LoginResponse
        {
            AccessToken = IssueAccessToken(user.Id, user.TenantId),
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
            AccessToken = IssueAccessToken(userId, tenantId),
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