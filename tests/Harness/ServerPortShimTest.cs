namespace Saas.Identity.AspNetCore.Tests.Harness;

using Microsoft.Extensions.Configuration;
using Saas.Identity.AspNetCore.Hosting;
using Xunit;

/// <summary>
/// SERVER_PORT shim 契约测试（conventions §6 全家族统一监听 key）：
/// - SERVER_PORT 存在且 ASPNETCORE_URLS 缺席 → 返回 http://+:{port}
/// - ASPNETCORE_URLS 存在 → 返回 null（框架原生 key 优先，容器内 Dockerfile ENV 即此路径）
/// - 两者都缺 → null（Kestrel 默认）
///
/// 与 lab-management-system-aspnetcore tests/Harness/ServerPortShimTest.cs 同构。
/// 不挂 [Trait("Fn", ...)]：脚手架级，不属于项目功能清单。
/// </summary>
public class ServerPortShimTest
{
    private static string? Resolve(params (string Key, string? Value)[] settings)
    {
        var dict = settings
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
        return ServerPortShim.ResolveUrls(new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build());
    }

    [Fact]
    public void ServerPort_setsUrls_whenAspnetcoreUrlsAbsent()
    {
        Assert.Equal("http://+:5099", Resolve(("SERVER_PORT", "5099")));
    }

    [Fact]
    public void AspnetcoreUrls_wins_whenBothPresent()
    {
        Assert.Null(Resolve(("SERVER_PORT", "5099"), ("ASPNETCORE_URLS", "http://localhost:5100")));
    }

    [Fact]
    public void NeitherSet_returnsNull()
    {
        Assert.Null(Resolve());
    }

    [Fact]
    public void EmptyServerPort_returnsNull()
    {
        Assert.Null(Resolve(("SERVER_PORT", "")));
    }
}
