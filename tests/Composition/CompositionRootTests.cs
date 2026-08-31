using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Services;
using Xunit;
using AppEntity = Saas.Identity.AspNetCore.Domain.Entities.App;
using MenuEntity = Saas.Identity.AspNetCore.Domain.Entities.Menu;
using UserEntity = Saas.Identity.AspNetCore.Domain.Entities.User;

namespace Saas.Identity.AspNetCore.Tests.Composition;

/// <summary>
/// 组合根 E2E（回归 2026-08-28 容器启动即崩）：
/// 所有既有测试手工 new SaasSessionStore() 构造控制器，从不经过 Program.cs，
/// 导致 SaasSessionStore / FailedLoginStore 未注册 DI + SaasSessionMiddleware
/// 未接线只在 prod 容器 ValidateOnBuild 时暴露。
/// 本测试走真实 Program 组合根（WebApplicationFactory，Development 环境
/// 默认 ValidateOnBuild），任何注册缺失 / middleware 漏接线都会在这里红。
/// </summary>
public class CompositionRootTests
{
    private const string Username = "e2e-alice";
    private const string Password = "dev123456";
    private static readonly Guid AppId = Guid.NewGuid();

    private static WebApplicationFactory<Program> NewFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("JWT_SIGNING_KEY", "e2e-signing-key-32-bytes-minimum!!");
                b.UseSetting("JWT_ISSUER", "saas-e2e");
                b.UseSetting("JWT_AUDIENCE", "saas-e2e-clients");
                // 假连接串即可 — NpgsqlDataSource.Build() 不连库；
                // AppDbContext 被替换成 InMemory（见下）
                b.UseSetting("DATABASE_URL", "Host=localhost;Database=e2e;Username=e2e;Password=e2e");
                // ConfigureTestServices（非 ConfigureServices）：minimal hosting 下前者
                // 在 Program.cs 注册之后执行，RemoveAll 才能真正替掉 Npgsql DbContext
                b.ConfigureTestServices(services =>
                {
                    // Program.cs 注册的 Npgsql DbContext 整体替换为 InMemory。
                    // 不能用 AddDbContext<AppDbContext, FlowTestDbContext>：EF 把
                    // DbContextOptions 注册到 Impl 类型上（dotnet/efcore#22758），
                    // 而 FlowTestDbContext 构造函数要 DbContextOptions<AppDbContext>，
                    // 所以手工注册 options + 工厂。
                    services.RemoveAll(typeof(AppDbContext));
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    var dbOpts = new DbContextOptionsBuilder<AppDbContext>()
                        .UseInMemoryDatabase($"e2e-{Guid.NewGuid()}")
                        .Options;
                    services.AddSingleton(dbOpts);
                    services.AddScoped<AppDbContext>(sp =>
                        new Controllers.OauthControllerTests.OauthSessionFlow.FlowTestDbContext(
                            sp.GetRequiredService<DbContextOptions<AppDbContext>>()));
                });
            });

    private static void Seed(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Username = Username,
            DisplayName = "E2E Alice",
            Email = "e2e@lab.local",
            PasswordHash = $"plain:{Password}",
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.Apps.Add(new AppEntity
        {
            Id = AppId,
            Code = "lab-management",
            Name = "lab-mgmt",
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.Menus.Add(new MenuEntity
        {
            Id = Guid.NewGuid(), AppId = AppId, Code = "m-dashboard", Name = "工作台",
            Path = "/", Type = "page", Status = "active", SortOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task CompositionRoot_builds_and_session_pipeline_works_end_to_end()
    {
        using var factory = NewFactory();
        Seed(factory);
        var client = factory.CreateClient();

        // 1. 组合根可构建（SaasSessionStore / FailedLoginStore 已注册）
        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        // 2. login 写 saasSession cookie（CreateClient 默认自动携带）
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { username = Username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // 3. 带 cookie 调 /me/menus — SaasSessionMiddleware 注入 session 后必须 200
        //    （middleware 未接线时 Items 恒空 -> 401）
        var menus = await client.GetAsync("/api/v1/me/menus");
        Assert.Equal(HttpStatusCode.OK, menus.StatusCode);
    }

    [Fact]
    public async Task CompositionRoot_withoutCookie_meMenus_returns401()
    {
        using var factory = NewFactory();
        Seed(factory);
        var client = factory.CreateClient();

        var menus = await client.GetAsync("/api/v1/me/menus");
        Assert.Equal(HttpStatusCode.Unauthorized, menus.StatusCode);
    }

    // M06.F03.I01 组合根断言 —— IAuditWriter 必须注册。未注册时 ValidateOnBuild 在
    // prod 容器暴露；本测试沿用 composition root 盲区套路（记忆：composition-root-blind-spot）。
    [Fact]
    public void CompositionRoot_registers_IAuditWriter()
    {
        using var factory = NewFactory();
        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        Assert.NotNull(writer);
    }
}
