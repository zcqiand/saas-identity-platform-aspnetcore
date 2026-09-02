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
        // InMemory 不支持 PG 专属映射（jsonb / native enum / 数组），
        // Ignore 掉 Auth 流程不触表的实体（同 OauthControllerTests.TestAppDbContext 模式）
        return new InMemoryTestDbContext(opts);
    }

    private sealed class InMemoryTestDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<Saas.Identity.AspNetCore.Domain.Entities.AuditEvent>();
            modelBuilder.Ignore<Saas.Identity.AspNetCore.Domain.Entities.AuditRetentionPolicy>();
            modelBuilder.Ignore<Saas.Identity.AspNetCore.Domain.Entities.ApiKey>();
            modelBuilder.Ignore<Saas.Identity.AspNetCore.Domain.Entities.Menu>();
            // Auth 流程触 Tenants 表（seed user 挂 tenant），实体不能整个 Ignore；
            // jsonb Settings InMemory 不支持，属性级 Ignore
            modelBuilder.Entity<Saas.Identity.AspNetCore.Domain.Entities.Tenant>()
                .Ignore(t => t.Settings);
        }
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
        var controller = new AuthController(db, NewJwt(), sessionStore, failedStore, new NoopAuditWriter())
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

    // === M03.F02.I04 — 2026-08-31 contract-test M96.F02.I24 抓获 ===
    // 未知 refreshToken 必须拒绝（ Unauthorized → 401/400 契约面），不能静默重发。

    [Fact]
    [Trait("Fn", "M03.F02.I04")]
    public async Task Refresh_unknownToken_throws()
    {
        var (controller, _) = NewController(NewDb());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.Refresh(new TokenRequest
            {
                GrantType = TokenRequestGrantType.Refresh_token,
                RefreshToken = "saas-rt-00000000-0000-0000-0000-00000000dead-0-xyz",
                ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TenantId = Guid.NewGuid(),
            }));
    }

    [Fact]
    [Trait("Fn", "M03.F02.I04")]
    public async Task Refresh_malformedToken_throws()
    {
        var (controller, _) = NewController(NewDb());
        // 非 refresh-/saas-rt- 前缀的垃圾 token 同样拒绝
        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.Refresh(new TokenRequest
            {
                GrantType = TokenRequestGrantType.Refresh_token,
                RefreshToken = "garbage",
                ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TenantId = Guid.NewGuid(),
            }));
    }

    // === M03.F03.I05 ===

    [Fact]
    [Trait("Fn", "M03.F03.I05")]
    public async Task Logout_completes()
    {
        var (controller, _) = NewController(NewDb());
        await controller.Logout();
    }
}