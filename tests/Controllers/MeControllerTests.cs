using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M00.F02 当前用户身份（whoami + 跨租户切换 + 我的菜单）。
/// </summary>
public class MeControllerTests
{
    private readonly MeController _c = new();

    [Fact]
    [Trait("Fn", "M00.F02.I01")]
    public async Task Whoami_returnsCurrentUser()
    {
        var u = await _c.Me();
        Assert.Equal("alice@acme.io", u.Email);
        Assert.NotEmpty(u.Memberships);
    }

    [Fact]
    [Trait("Fn", "M00.F02.I02")]
    public async Task Tenants_returnsMemberships()
    {
        var m = await _c.Tenants();
        Assert.NotEmpty(m);
    }

    [Fact]
    [Trait("Fn", "M00.F02.I03")]
    public async Task Switch_returnsNewToken()
    {
        var res = await _c.Switch(InMemoryStore.GlobexId.ToString());
        Assert.Contains(InMemoryStore.GlobexId.ToString(), res.AccessToken);
    }

    [Fact]
    [Trait("Fn", "M09.F03.I04")]
    public async Task Menus_returnsEffectiveMenuTree()
    {
        var menus = await _c.Menus();
        Assert.NotEmpty(menus);
    }
}