using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Saas.Identity.AspNetCore.Security;

/// <summary>
/// M03.F01.I01 — saas session cookie 中间件。
///
/// 解析 Cookie `saasSession=<sid>`（HttpOnly + SameSite=Lax + Secure 由调用方在
/// AuthController.Login 写 Set-Cookie 时设），从 SaasSessionStore 读 session，
/// 注入 `HttpContext.Items["saasSession"]`。过期 / 不存在则不注入 — 调用方
/// 看到 null 即可返 401。
///
/// 注册位置（Phase 1 — Program.cs）：
///   app.UseMiddleware<SaasSessionMiddleware>();
///   app.UseAuthentication();
///   app.UseAuthorization();
///   app.MapControllers();
/// </summary>
public sealed class SaasSessionMiddleware
{
    public const string CookieName = "saasSession";
    public const string ItemsKey = "saasSession";

    private readonly SaasSessionStore _store;
    private readonly RequestDelegate _next;

    public SaasSessionMiddleware(SaasSessionStore store, RequestDelegate next)
    {
        _store = store;
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var sid = context.Request.Cookies[CookieName];
        var session = _store.Get(sid);
        if (session is not null)
        {
            context.Items[ItemsKey] = session;
        }
        return _next(context);
    }
}