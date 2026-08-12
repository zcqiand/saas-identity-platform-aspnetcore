using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M04.F03 OAuth 授权码签发 + 令牌交换/刷新。
/// 公开端点（OAuth Provider）。
/// </summary>
public class OauthController : OauthControllerBase
{
    public override Task<Response3> Authorize(AuthorizeCodeRequest body)
    {
        // M04.F03.I07 授权码签发
        return Task.FromResult(new Response3
        {
            Code = Guid.NewGuid().ToString("N"),
            State = "",
        });
    }

    public override Task<TokenResponse> Token(TokenRequest body)
    {
        // M04.F03.I08 令牌交换
        return Task.FromResult(new TokenResponse
        {
            AccessToken = "oauth-access-token",
            RefreshToken = "oauth-refresh-token",
            TokenType = "Bearer",
            ExpiresIn = 3600,
        });
    }
}