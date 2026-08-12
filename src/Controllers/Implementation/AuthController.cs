using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M03.F01 密码登录 + M03.F02 OIDC 回调 + M03.F03 登出。
/// 公开端点（不需要 TenantGuard）。
/// </summary>
public class AuthController : AuthControllerBase
{
    public override Task<LoginResponse> Login(LoginRequest body)
    {
        // M03.F01.I01 账号密码登录
        var user = InMemoryStore.Users.FirstOrDefault(u => u.Username == body.Username);
        if (user == null || string.IsNullOrEmpty(body.Password))
            throw new UnauthorizedAccessException("invalid credentials");

        return Task.FromResult(new LoginResponse
        {
            AccessToken = $"mock-jwt-{user.Id}",
            RefreshToken = "mock-refresh",
            TokenType = "Bearer",
            ExpiresIn = 3600,
            UserId = user.Id,
            CurrentTenantId = user.TenantId,
        });
    }

    public override Task Logout()
    {
        // M03.F03.I05/06 登出（本地清理 + 全局 SSO）
        return Task.CompletedTask;
    }

    public override Task<TokenResponse> Callback(OidcCallbackRequest body)
    {
        // M03.F02.I03 OIDC Code 换取
        return Task.FromResult(new TokenResponse
        {
            AccessToken = "oidc-access-token",
            TokenType = "Bearer",
            ExpiresIn = 3600,
        });
    }

    public override Task<TokenResponse> Refresh(TokenRequest body)
    {
        // M03.F02.I04 refresh token
        return Task.FromResult(new TokenResponse
        {
            AccessToken = "refreshed-access-token",
            TokenType = "Bearer",
            ExpiresIn = 3600,
        });
    }
}