using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using Saas.Identity.AspNetCore.Security;
using DbSysUser = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysUser;
using DbTenant = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.Tenant;
using DbTenantMember = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantMember;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// 5.34 — 错误语义收口（池项 5.34 / live 对拍 I22/I39/I70 的测试级锁）：
///   a) PATCH /tenants/{t}/members/{u} 合法 body 对已存在成员 → 200
///      （NRT 隐式 Required 推断不得把请求拦成 400）；
///   b) 同 PATCH 不存在 userId → 404，envelope code NOT_FOUND；
///   c) POST /auth/login 错密码 → 401，envelope code UNAUTHORIZED。
/// 走真管线（WebApplicationFactory + InMemory DB）：JWKS 同 key HS256 自签 token 过
/// TenantGuard，body 反序列化/校验/validation 全在 MVC 管线里，控制器单元测试盖不住。
/// envelope 形状见 Program.cs UseExceptionHandler：5.13-② 起全部错误统一契约
/// ErrorResponse { code, message }（锁定错误另有 LockedAccountResponse 的 lockedUntil）。
/// </summary>
public class ErrorSemanticsTests : IClassFixture<ErrorSemanticsTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Program.cs fail-fast 三件套（ADR-0019 / 2026-08-30）必须给足：
            // JWT_SIGNING_KEY ≥32B、SAAS_CORS_ALLOWED_ORIGINS、DATABASE_URL（InMemory 替换后不真连）。
            builder.UseSetting("JWT_SIGNING_KEY", "unit-test-signing-key-0123456789abcdef-32b!");
            builder.UseSetting("JWT_ISSUER", "https://unit-test-534.local");
            builder.UseSetting("JWT_AUDIENCE", "unit-test-534");
            builder.UseSetting("SAAS_CORS_ALLOWED_ORIGINS", "http://localhost:5173");
            builder.UseSetting("DATABASE_URL", "Host=localhost;Port=5432;Database=unused_534;Username=test;Password=test");
            builder.ConfigureServices(services =>
            {
                // InMemory 替身（csproj 既有 8.0.10 依赖，PartialUpdateSemanticsTests 同源）：
                // 替换 Program 的 Npgsql DbContextOptions，不碰 PG。
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("err-semantics-534"));
            });
        }
    }

    private readonly Factory _factory;

    public ErrorSemanticsTests(Factory factory) => _factory = factory;

    private const string Password = "right-pass-534";

    private (Guid TenantId, Guid UserId, string Username) SeedActiveMember(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new DbTenant
        {
            Id = Guid.NewGuid(),
            TenantKey = $"t-{username}",
            Name = $"tenant-{username}",
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var user = new DbSysUser
        {
            Id = Guid.NewGuid(),
            Username = username,
            Password = $"plain:{Password}",
            Email = "old@example.com",
            Status = 1, // active
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var member = new DbTenantMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = user.Id,
            Status = 1, // active
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Tenants.Add(tenant);
        db.SysUsers.Add(user);
        db.TenantMembers.Add(member);
        db.SaveChanges();
        return (tenant.Id, user.Id, user.Username);
    }

    private HttpClient ClientFor(Guid tenantId)
    {
        var jwt = _factory.Services.GetRequiredService<JwtIssuer>()
            .IssueAccessTokenForTest(Guid.NewGuid().ToString(), tenantId.ToString());
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", jwt);
        return client;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    [Fact]
    [Trait("Fn", "M00.F02.I04")]
    public async Task MembersPatch_existingMember_legalBody_returns200()
    {
        var (tenantId, userId, _) = SeedActiveMember($"u-patch-{Guid.NewGuid():N}");
        using var client = ClientFor(tenantId);

        // 合法 body：email/mobile + 契约外 status 字段 —— 5.13-① 起 UpdateSysUserRequest
        // 已无 status（状态唯一通道 /status），body 里的 "status":"active" 是未知属性，
        // 必须被容忍（200，System.Text.Json 扩展数据忽略），不得 400/改写成员状态。
        var resp = await client.PatchAsync(
            $"api/v1/tenants/{tenantId}/members/{userId}",
            new StringContent(
                """{"email":"patched@example.com","mobile":"13900000000","status":"active"}""",
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await BodyAsync(resp);
        Assert.Equal("patched@example.com", body.GetProperty("email").GetString());
        // 5.34 converter 语义回显锁：响应 status 枚举必须序列化为 snake_case 字符串
        // （非泛型 JsonStringEnumConverter(SnakeCaseLower)，Program.cs 2026-09-12 修复），
        // 不是数字（1）也不是 PascalCase（"Active"）。
        Assert.Equal("active", body.GetProperty("status").GetString());
    }

    [Fact]
    [Trait("Fn", "M00.F02.I04")]
    public async Task MembersPatch_unknownUserId_returns404NotFound()
    {
        var (tenantId, _, _) = SeedActiveMember($"u-patch404-{Guid.NewGuid():N}");
        using var client = ClientFor(tenantId);
        var ghost = Guid.NewGuid(); // 合法 GUID，但 (tenantId, userId) 无 member 行

        var resp = await client.PatchAsync(
            $"api/v1/tenants/{tenantId}/members/{ghost}",
            new StringContent(
                """{"email":"ghost@example.com","status":"active"}""",
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await BodyAsync(resp);
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Fn", "M01.F04.I01")]
    public async Task Login_wrongPassword_returns401Unauthorized()
    {
        var (_, _, username) = SeedActiveMember($"u-login-{Guid.NewGuid():N}"); // 用户存在、密码错 → 真错密码分支
        using var client = _factory.CreateClient();

        var resp = await client.PostAsync(
            "api/v1/auth/login",
            new StringContent(
                $$"""{"username":"{{username}}","password":"definitely-wrong","clientId":""}""",
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await BodyAsync(resp);
        Assert.Equal("UNAUTHORIZED", body.GetProperty("code").GetString());
    }
}
