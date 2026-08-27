using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Auth.Session;

/// <summary>
/// M03.F01.I01 — SaasSessionMiddleware（cookie saasSession -> HttpContext.Items 注入）。
/// ADR-0013 路线 A：所有 OAuth / Me 端点检查 session 中间件注入的 session。
/// </summary>
public class SaasSessionMiddlewareTest
{
    private static async Task<HttpContext> Run(SaasSessionStore store, string? cookieValue)
    {
        var middleware = new SaasSessionMiddleware(store, _ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        if (cookieValue != null)
        {
            ctx.Request.Headers["Cookie"] = $"saasSession={cookieValue}";
        }
        await middleware.InvokeAsync(ctx);
        return ctx;
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public async Task ValidCookie_injectsSessionIntoItems()
    {
        var store = new SaasSessionStore();
        var session = new SaasSession("user-1", System.Guid.NewGuid(),
            System.DateTime.UtcNow, System.DateTime.UtcNow.AddHours(1));
        store.Put(session);

        var ctx = await Run(store, session.Id);
        var got = ctx.Items["saasSession"] as SaasSession;
        Assert.NotNull(got);
        Assert.Equal("user-1", got!.UserId);
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public async Task NoCookie_doesNotInject()
    {
        var store = new SaasSessionStore();
        var ctx = await Run(store, null);
        Assert.False(ctx.Items.ContainsKey("saasSession"));
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public async Task UnknownCookieValue_doesNotInject()
    {
        var store = new SaasSessionStore();
        var ctx = await Run(store, "unknown-id");
        Assert.False(ctx.Items.ContainsKey("saasSession"));
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public async Task ExpiredCookie_doesNotInject()
    {
        var store = new SaasSessionStore(TimeSpan.FromMilliseconds(100));
        var session = new SaasSession("user-2", System.Guid.NewGuid(),
            System.DateTime.UtcNow, System.DateTime.UtcNow.AddMilliseconds(100));
        store.Put(session);
        await System.Threading.Tasks.Task.Delay(200);

        var ctx = await Run(store, session.Id);
        Assert.False(ctx.Items.ContainsKey("saasSession"));
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public async Task Next_invoked_always()
    {
        var store = new SaasSessionStore();
        var nextCalled = false;
        var middleware = new SaasSessionMiddleware(store, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var ctx = new DefaultHttpContext();
        await middleware.InvokeAsync(ctx);
        Assert.True(nextCalled);
    }
}