using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M04.F03 OAuth 授权码签发 + 令牌交换（公开端点）。
/// </summary>
public class OauthControllerTests
{
    private readonly OauthController _c = new();

    [Fact]
    [Trait("Fn", "M04.F03.I07")]
    public async Task Authorize_returnsCode()
    {
        var res = await _c.Authorize(new()
        {
            ClientId = Guid.NewGuid(),
            RedirectUri = "https://app/cb",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "openid",
            State = "xyz",
        });
        Assert.False(string.IsNullOrEmpty(res.Code));
    }

    [Fact]
    [Trait("Fn", "M04.F03.I08")]
    public async Task Token_exchangesForAccess()
    {
        var res = await _c.Token(new()
        {
            GrantType = TokenRequestGrantType.Authorization_code,
            ClientId = Guid.NewGuid(),
            Code = "auth-code",
            RedirectUri = "https://app/cb",
        });
        Assert.False(string.IsNullOrEmpty(res.AccessToken));
    }
}