using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M03 认证（公开端点）：登录 + 登出 + OIDC + refresh。
/// </summary>
public class AuthControllerTests
{
    private readonly AuthController _c = new(null!, null!);

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M03.F01.I01")]
    public async Task Login_validCredentials_returnsTokens()
    {
        var res = await _c.Login(new() { Username = "alice", Password = "x" });
        Assert.StartsWith("mock-jwt-", res.AccessToken);
        Assert.Equal("Bearer", res.TokenType);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M03.F01.I01")]
    public async Task Login_invalidUsername_throws()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _c.Login(new() { Username = "nobody", Password = "x" }));
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M03.F02.I03")]
    public async Task Callback_exchangesCode()
    {
        var res = await _c.Callback(new() { Code = "abc", State = "x", ClientId = Guid.NewGuid() });
        Assert.False(string.IsNullOrEmpty(res.AccessToken));
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M03.F02.I04")]
    public async Task Refresh_returnsNewToken()
    {
        var res = await _c.Refresh(new() { RefreshToken = "old", ClientId = Guid.NewGuid(), GrantType = TokenRequestGrantType.Refresh_token });
        Assert.False(string.IsNullOrEmpty(res.AccessToken));
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M03.F03.I05")]
    public async Task Logout_completes()
    {
        await _c.Logout();
    }
}