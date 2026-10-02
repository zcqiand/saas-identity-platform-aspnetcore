using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// REQ-2026-001 — 后端根路径默认跳转 Swagger UI。
/// 基础设施端点：影响功能数 0，不进功能树，禁挂 [Trait("Fn", ...)]（挂假 ID = L5 悬空引用；
/// trace.json 由 gen-trace.py 硬编码清单产出，本测试不进清单即不进 trace）。
/// 走真管线（WebApplicationFactory，ErrorSemanticsTests 同模式）：
///   AC-1 匿名 GET / → 302 Location:/swagger，/swagger UI 与 swagger.json 可用；
///   AC-2 带 Authorization 行为一致（端点匿名，坏 token 不改道）；
///   AC-3 /api/* 与 /health 不被根端点吞；根端点在 SaasSessionMiddleware 语义之外
///        ——该中间件对无 cookie 请求是纯透传（见 SaasSessionMiddleware.InvokeAsync），
///        302 响应不设不读 saasSession；
///   AC-4 根跳转端点不出现在 openapi 文档。
/// ADR-0019 fail-fast 三件套 env 显式给足；InMemory 替身与 ErrorSemanticsTests 同源
///（本用例不查库，DATABASE_URL 仅占位）。
/// </summary>
public class RootSwaggerRedirectTests : IClassFixture<RootSwaggerRedirectTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("JWT_SIGNING_KEY", "unit-test-signing-key-0123456789abcdef-32b!");
            builder.UseSetting("JWT_ISSUER", "https://unit-test-req001.local");
            builder.UseSetting("JWT_AUDIENCE", "unit-test-req001");
            builder.UseSetting("SAAS_CORS_ALLOWED_ORIGINS", "http://localhost:5173");
            builder.UseSetting("DATABASE_URL", "Host=localhost;Port=5432;Database=unused_req001;Username=test;Password=test");
            builder.ConfigureServices(services =>
            {
                // InMemory 替身（csproj 既有 8.0.10 依赖）：替换 Program 的 Npgsql DbContextOptions，不碰 PG。
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("root-redirect-req001"));
            });
        }
    }

    private readonly Factory _factory;

    public RootSwaggerRedirectTests(Factory factory) => _factory = factory;

    private HttpClient NoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task RootPath_anonymousGet_redirects302ToSwagger()
    {
        using var client = NoRedirectClient();

        using var resp = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/swagger", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task RootPath_withAuthorizationHeader_stillRedirects302()
    {
        using var client = NoRedirectClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-real-token");

        using var resp = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/swagger", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task SwaggerUi_servesAtSwaggerPrefix()
    {
        using var client = NoRedirectClient();

        // Swashbuckle UI（RoutePrefix="swagger"）：GET /swagger 301/302 → /swagger/index.html（UI 实际路由），
        // 跟随后 200 可渲染（AC-1「跟随跳转后 UI 200」）。Swashbuckle 发相对 Location（无前导斜杠）。
        using var resp = await client.GetAsync("/swagger");
        Assert.Contains(resp.StatusCode,
            new[] { HttpStatusCode.MovedPermanently, HttpStatusCode.Found });
        Assert.EndsWith("swagger/index.html", resp.Headers.Location?.ToString());

        using var indexResp = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, indexResp.StatusCode);
    }

    [Fact]
    public async Task SwaggerDoc_servesOpenApiJson_withoutRootPath()
    {
        using var client = NoRedirectClient();

        using var resp = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("openapi", out _), "swagger.json 缺 openapi 版本键");
        Assert.False(doc.RootElement.GetProperty("paths").TryGetProperty("/", out _),
            "AC-4：根跳转端点不得出现在 openapi 文档");
    }

    [Fact]
    public async Task InfraRoutes_apiAndHealth_notSwallowedByRedirect()
    {
        using var client = NoRedirectClient();

        // AC-3：/api/* 不被根端点吞（未知路径仍 404，不是 302）
        using var apiResp = await client.GetAsync("/api/v1/__req001_no_such_route__");
        Assert.Equal(HttpStatusCode.NotFound, apiResp.StatusCode);

        // AC-3：/health 探针保持 200（CT 配方 / deploy 探活依赖它）
        using var healthResp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
    }
}
