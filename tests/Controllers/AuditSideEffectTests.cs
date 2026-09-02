using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Services;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// 2026-09-02 contract-test M96 audit 覆盖对齐：login_success + user_created 写端点副作用。
/// msw/nextjs 已写这些事件，本仓此前缺失 → audit 列表 4 后端对前端不可区分破裂。
/// mock IAuditWriter 验证调用形状（不触 AuditEvent 表 → InMemory provider 可跑，
/// 绕开 AuthControllerTests 全员 Skip 的 Metadata 映射限制）。
/// </summary>
public class AuditSideEffectTests
{
    private static AppDbContext NewDb()
    {
        var name = $"audit-test-{Guid.NewGuid()}";
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .UseApplicationServiceProvider(new ServiceCollection().BuildServiceProvider())
            .EnableServiceProviderCaching(false)
            .Options;
        // 同 AuthControllerTests.InMemoryTestDbContext：Ignore PG 专属映射实体
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

    private static async Task<(AuthController, DefaultHttpContext)> SeedAndBuild(
        AppDbContext db, Mock<IAuditWriter> audit)
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
            Username = "alice",
            DisplayName = "alice",
            Email = "alice@example.com",
            PasswordHash = "plain:dev123456",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var ctx = new DefaultHttpContext();
        var controller = new AuthController(db, NewJwt(), new SaasSessionStore(), new FailedLoginStore(), audit.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };
        return (controller, ctx);
    }

    // M03.F01.I01 — login 成功写 login_success：actor=target=登录用户，metadata={username}
    // （形状对齐 nextjs app/api/v1/auth/login/route.ts 与 springboot AuthService）
    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public async Task Login_success_writesAuditEvent()
    {
        using var db = NewDb();
        var audit = new Mock<IAuditWriter>();
        var (controller, _) = await SeedAndBuild(db, audit);

        await controller.Login(new() { Username = "alice", Password = "dev123456" });

        var user = db.Users.First();
        audit.Verify(w => w.WriteAsync(
            user.TenantId.ToString(),
            user.Id.ToString(),
            "login_success",
            null,
            It.Is<IDictionary<string, object?>>(m =>
                m.ContainsKey("username") && (string?)m["username"] == "alice"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // M01.F01.I02 — 创建用户写 user_created：metadata={userId}
    [Fact]
    [Trait("Fn", "M01.F01.I02")]
    public async Task UsersPost_writesAuditEvent()
    {
        using var db = NewDb();
        var audit = new Mock<IAuditWriter>();
        var (_, _) = await SeedAndBuild(db, audit);
        // path tenantId 运行时是真 UUID（Guid.Parse），StubTenantContext 须同值过 guard
        var tenantUuid = db.Tenants.First().Id.ToString();
        var users = new TenantUsersController(
            new TenantGuard(new StubTenantContext { TenantId = tenantUuid }),
            db, audit.Object, new HttpContextAccessor());

        var created = await users.UsersPost(tenantUuid, new()
        {
            Username = "bob",
            Email = "bob@example.com",
            Password = "p",
        });

        audit.Verify(w => w.WriteAsync(
            tenantUuid,
            It.IsAny<string?>(),
            "user_created",
            created.Id.ToString(),
            It.Is<IDictionary<string, object?>>(m =>
                m.ContainsKey("userId") && (string?)m["userId"] == created.Id.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
