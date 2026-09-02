namespace Saas.Identity.AspNetCore.Hosting;

using Microsoft.Extensions.Configuration;

/// <summary>
/// SERVER_PORT → UseUrls 决策（conventions §6 全家族统一监听 key，aspnetcore=5104）。
///
/// ASP.NET Core 原生只认 ASPNETCORE_URLS；本 shim 让裸机/dotnet run 也能用
/// 与 springboot 仓同名的 SERVER_PORT。ASPNETCORE_URLS 优先（容器内 Dockerfile
/// ENV 已设 http://+:8080，prod 路径不受 shim 影响）。
/// 与 lab-management-system-aspnetcore src/Hosting/ServerPortShim.cs 同构。
/// </summary>
public static class ServerPortShim
{
    /// <summary>返回 shim 应用的 URLs；null = 不干预（ASPNETCORE_URLS 已设或都未设）。</summary>
    public static string? ResolveUrls(IConfiguration config)
    {
        var port = config["SERVER_PORT"];
        return string.IsNullOrEmpty(port) || !string.IsNullOrEmpty(config["ASPNETCORE_URLS"])
            ? null
            : $"http://+:{port}";
    }
}
