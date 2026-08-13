using System.Text;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M04 OAuth 授权码签发 + 令牌交换。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext（apps / oauth code 表 Phase 6 引入）。
/// 当前 Phase 5 简化：返回 mock token；客户端 redirect_uri / state 完整实现留 Phase 6。
/// </summary>
public class OauthController : OauthControllerBase
{
    private readonly AppDbContext _db;
    public OauthController(AppDbContext db) { _db = db; }

    private static string B64Url(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .Replace("=", "").Replace("+", "-").Replace("/", "_");

    public override Task<Response3> Authorize(AuthorizeCodeRequest body)
    {
        // Phase 6: 校验 client_id 在 apps 表存在；校验 scope；存 oauth_codes 表
        return Task.FromResult(new Response3
        {
            Code = Guid.NewGuid().ToString("N"),
            State = body.State,
        });
    }

    public override Task<TokenResponse> Token(TokenRequest body)
    {
        // Phase 6: 校验 code 存在且未过期；查 user + tenant 返回 JWT
        return Task.FromResult(new TokenResponse
        {
            AccessToken = $"oauth-access-token-{Guid.NewGuid():N}",
            RefreshToken = $"oauth-refresh-token-{Guid.NewGuid():N}",
            TokenType = "Bearer",
            ExpiresIn = 3600,
        });
    }
}