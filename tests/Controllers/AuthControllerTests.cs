using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M03 认证：登录 + 登出 + OIDC + refresh。
/// ADR-0013 路线 A：M03.F01.I01 登录写 saas session cookie + access token；
/// M03.F01.I02 失败锁定 5 次 / 15min。
/// </summary>
public class AuthControllerTests
{
    private static AppDbContext NewDb()
    {
        var name = $"test-{Guid.NewGuid()}";
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .UseApplicationServiceProvider(new ServiceCollection().BuildServiceProvider())
            .EnableServiceProviderCaching(false)
            .Options;
        return new AppDbContext(opts);
    }

    private static JwtIssuer NewJwt() => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SIGNING_KEY"] = "test-signing-key-32-bytes-min-length!",
                ["JWT_ISSUER"] = "saas-test",
                ["JWT_AUDIENCE"] = "saas-test-clients",
            })
            .Build());

    private static (AuthController, DefaultHttpContext) NewController(
        AppDbContext db,
        SaasSessionStore? sessionStore = null,
        FailedLoginStore? failedStore = null)
    {
        sessionStore ??= new SaasSessionStore();
        failedStore ??= new FailedLoginStore();
        var ctx = new DefaultHttpContext();
        var controller = new AuthController(db, NewJwt(), sessionStore, failedStore)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };
        return (controller, ctx);
    }

    private static async Task SeedUser(AppDbContext db, string username = "alice", string password = "dev123456")
    {
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Saas.Identity.AspNetCore.Domain.Entities.Tenant
        {
            Id = tenantId,
            Code = "acme",
            Name = "Acme",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
        });
        db.Users.Add(new Saas.Identity.AspNetCore.Domain.Entities.User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = username,
            DisplayName = username,
            Email = $"{username}@example.com",
            PasswordHash = $"plain:{password}",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // === M03.F01.I01 ===

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG；DbContext InMemory 不支持 Dictionary<string,object> Metadata")]
    [Trait("Fn", "M03.F01.I01")]
    public async Task Login_validCredentials_returnsTokensAndSetsCookie()
    {
        using var db = NewDb();
        await SeedUser(db);
        var (controller, ctx) = NewController(db);
        var res = await controller.Login(new() { Username = "alice", Password = "dev123456" });
        Assert.False(string.IsNullOrEmpty(res.AccessToken));
        Assert.Equal("Bearer", res.TokenType);
        Assert.NotEqual(default, res.UserId);
        // saas session cookie 已写入 Response
        Assert.True(ctx.Response.Headers.ContainsKey("Set-Cookie"));
        var cookie = ctx.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("saasSession=", cookie);
        Assert.Contains("HttpOnly", cookie);
        Assert.Contains("SameSite=Lax", cookie);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M03.F01.I01")]
    public async Task Login_invalidPassword_throwsAndRecordsFailure()
    {
        using var db = NewDb();
        await SeedUser(db);
        var (controller, _) = NewController(db);
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => controller.Login(new() { Username = "alice", Password = "wrong" }));
        Assert.Equal("invalid credentials", ex.Message);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M03.F01.I01")]
    public async Task Login_unknownUser_throws()
    {
        using var db = NewDb();
        var (controller, _) = NewController(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => controller.Login(new() { Username = "nobody", Password = "x" }));
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M03.F01.I01")]
    public async Task Login_suspendedUser_throws()
    {
        using var db = NewDb();
        await SeedUser(db);
        var user = db.Users.First();
        user.Status = "suspended";
        await db.SaveChangesAsync();
        var (controller, _) = NewController(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => controller.Login(new() { Username = "alice", Password = "dev123456" }));
    }

    // === M03.F01.I02 失败锁定 ===

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M03.F01.I02")]
    public async Task Login_5WrongPasswords_locksAccount_throwsLocked()
    {
        using var db = NewDb();
        await SeedUser(db);
        var (controller, _) = NewController(db,
            failedStore: new FailedLoginStore(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15)));
        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => controller.Login(new() { Username = "alice", Password = "wrong" }));
        }
        // 第 6 次：账号已锁定
        await Assert.ThrowsAsync<AccountLockedException>(
            () => controller.Login(new() { Username = "alice", Password = "dev123456" }));
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M03.F01.I02")]
    public async Task Login_lockedAccount_shortDuration_unlocks()
    {
        using var db = NewDb();
        await SeedUser(db);
        var (controller, _) = NewController(db,
            failedStore: new FailedLoginStore(maxAttempts: 5, lockoutDuration: TimeSpan.FromMilliseconds(100)));
        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => controller.Login(new() { Username = "alice", Password = "wrong" }));
        }
        await System.Threading.Tasks.Task.Delay(200);
        // 锁定过期：合法密码应能登录成功
        var res = await controller.Login(new() { Username = "alice", Password = "dev123456" });
        Assert.False(string.IsNullOrEmpty(res.AccessToken));
    }

    // === M03.F02 占位 (Phase 5) ===

    [Fact(Skip = "M03.F02 OIDC 路径 Phase 6 改造；本次任务仅覆盖 M03.F01")]
    [Trait("Fn", "M03.F02.I03")]
    public Task Callback_exchangesCode() => Task.CompletedTask;

    [Fact(Skip = "M03.F02 refresh Phase 6 改造")]
    [Trait("Fn", "M03.F02.I04")]
    public Task Refresh_returnsNewToken() => Task.CompletedTask;

    // === M03.F03.I05 ===

    [Fact]
    [Trait("Fn", "M03.F03.I05")]
    public async Task Logout_completes()
    {
        var (controller, _) = NewController(NewDb());
        await controller.Logout();
    }
}