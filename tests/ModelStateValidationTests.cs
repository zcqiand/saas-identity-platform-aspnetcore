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
using DbTenant = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.Tenant;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// 5.64 — 全局 ModelState→400（DTO DataAnnotations 运行时 enforce）。
/// NSwag regen 进 DTO 的 [Required]/[StringLength] 此前只进 ModelState、无 filter 消费
/// → live 实测 POST members 短密码（契约 @minLength(8)）真 200 建行。
/// 本批全局注册 ModelStateValidationFilter（与 MalformedBodyFilter 同位），锁：
///   a) password "short"（5 字符 <8）→ 400 INVALID_REQUEST（此前 200 —— 本测试红先行）；
///   b) 缺 [Required] 必填 username → 400（属性级校验失败同路径）；
///   c) 合法 body（password 恰 8 字符）→ 仍 200 —— 校验收紧不得误伤合规请求。
/// 走真管线（WebApplicationFactory + InMemory DB），写法参照 5.34 ErrorSemanticsTests。
/// 不挂 Fn trait：400 校验语义已由 M00.F02.I22 / 账号创建族覆盖（功能树无新增交互），
/// 本测试是契约约束回归锁，防 fnReporter 误吸（regression anchor 泄漏先例）。
/// </summary>
public class ModelStateValidationTests : IClassFixture<ModelStateValidationTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // 与 ErrorSemanticsTests.Factory 同款 fail-fast 三件套 + InMemory 替身
            builder.UseSetting("JWT_SIGNING_KEY", "unit-test-signing-key-0123456789abcdef-32b!");
            builder.UseSetting("JWT_ISSUER", "https://unit-test-564.local");
            builder.UseSetting("JWT_AUDIENCE", "unit-test-564");
            builder.UseSetting("SAAS_CORS_ALLOWED_ORIGINS", "http://localhost:5173");
            builder.UseSetting("DATABASE_URL", "Host=localhost;Port=5432;Database=unused_564;Username=test;Password=test");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("model-state-564"));
            });
        }
    }

    private readonly Factory _factory;

    public ModelStateValidationTests(Factory factory) => _factory = factory;

    private Guid SeedTenant(string key)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new DbTenant
        {
            Id = Guid.NewGuid(),
            TenantKey = key,
            Name = $"tenant-{key}",
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Tenants.Add(tenant);
        db.SaveChanges();
        return tenant.Id;
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

    private static StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");

    [Fact]
    public async Task MembersPost_shortPassword_returns400InvalidRequest()
    {
        var tenantId = SeedTenant($"t-msv-{Guid.NewGuid():N}");
        using var client = ClientFor(tenantId);
        var name = $"u-msv-short-{Guid.NewGuid():N}";

        // password 5 字符，契约 @minLength(8) —— 实现前真 200 建行（红）
        var resp = await client.PostAsync(
            $"api/v1/tenants/{tenantId}/members",
            Json($$"""{"username":"{{name}}","email":"{{name}}@x.io","password":"short"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await BodyAsync(resp);
        Assert.Equal("INVALID_REQUEST", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MembersPost_missingRequiredUsername_returns400()
    {
        var tenantId = SeedTenant($"t-msv-{Guid.NewGuid():N}");
        using var client = ClientFor(tenantId);

        // username 是 [Required] —— 缺失走同一 DTO 属性级校验路径
        var resp = await client.PostAsync(
            $"api/v1/tenants/{tenantId}/members",
            Json("""{"email":"nouser@example.com","password":"long-enough-8"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await BodyAsync(resp);
        Assert.Equal("INVALID_REQUEST", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MembersPost_validBody_minBoundaryPassword_stillSucceeds()
    {
        var tenantId = SeedTenant($"t-msv-{Guid.NewGuid():N}");
        using var client = ClientFor(tenantId);
        var name = $"u-msv-ok-{Guid.NewGuid():N}";

        // password 恰 8 字符（契约下限边界）—— filter 不得误伤合规请求
        var resp = await client.PostAsync(
            $"api/v1/tenants/{tenantId}/members",
            Json($$"""{"username":"{{name}}","email":"{{name}}@x.io","password":"12345678"}"""));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await BodyAsync(resp);
        Assert.Equal(name, body.GetProperty("username").GetString());
    }
}
