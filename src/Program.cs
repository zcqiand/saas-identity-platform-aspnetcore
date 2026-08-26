using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Controllers.Implementation;

// appsettings*.json 在仓根，csproj 已拷到 bin/。强制 ContentRoot = bin 目录，
// 这样不管 cwd 是 src/、仓根、还是生产部署的任意路径，配置文件都能被加载。
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// JWT bearer auth — tenant_id claim is mandatory for tenant-scoped routes
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Phase 4 env 对称化: env var 走 flat JWT_SIGNING_KEY/JWT_ISSUER/JWT_AUDIENCE (与 saas-springboot + saas-msw 镜像)
        // ASP.NET Core 默认 env provider 直接把 flat key 当 flat config 读, 不走 `:` 段映射
        // (`:` 段会变成 `__` 双下划线)。所以这里读 flat key, 与其他 6 仓命名对齐。
        options.Authority = builder.Configuration["JWT_AUTHORITY"];
        // v0.2.1 Phase 2B：删除 dev 分支 RequireSignedTokens=false + SignatureValidator（alg=none 占位路径）。
        // 现统一 HS256 真验签（RFC 7519），与 JwtIssuer 共享 JWT_SIGNING_KEY。
        // saas-identity-platform-msw (Phase 1A) + saas-identity-platform-nextjs-self 签出来的
        // token 都能被本仓 JwtBearer 走标准路径验签通过；不再需要 dev 兜底分支。
        //
        // Production：配 JWT_AUTHORITY (issuer-uri) 让 JwtBearer 自动切 JWKS；本 TokenValidationParameters
        // 在 prod profile 可被覆盖（appsettings.Production.json 重写）或整段删除走默认 JWKS。
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["JWT_ISSUER"],
            ValidAudience = builder.Configuration["JWT_AUDIENCE"],
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(builder.Configuration["JWT_SIGNING_KEY"] ?? "dev-key-32-bytes-minimum-length!")),
        };
    });

// CORS — 允许跨 origin 调本后端的白名单。默认给 dev：
//   - saas-nextjs :3000（saas 全栈 Next.js API routes 调 saas 后端）
//   - lab-react,vue :5173（lab 前端 dev server 调 saas 后端走 MSW switch）
//   - lab-nextjs :3001（lab 全栈 Next.js API routes 调 saas 后端）
// 生产用 SAAS_CORS_ALLOWED_ORIGINS env override（逗号分隔）改正式域名。
// 与 springboot 端的 SecurityConfig.corsConfigurationSource() 对称 — 同一 env var。
builder.Services.AddCors(options =>
{
    var origins = builder.Configuration["SAAS_CORS_ALLOWED_ORIGINS"]
        ?? "http://localhost:3000,http://localhost:5173,http://localhost:3001";
    options.AddPolicy("NextDev", policy =>
        policy.WithOrigins(origins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TenantContext>();
builder.Services.AddSingleton<TenantGuard>();
// JwtIssuer (v0.2.0 Phase 6)：HS256 签 access token，AuthController + OauthController 共用。
// 配置: JWT_SIGNING_KEY (≥32B), JWT_ISSUER, JWT_AUDIENCE。3 个 saas 后端用同一 key (共享 JWT)。
builder.Services.AddSingleton<JwtIssuer>();

// M10.Database — EF Core + Npgsql + snake_case 命名（ADR-0010）
// shared SQL 是 SSOT；EF Model 镜像；启动时**不调** Database.Migrate()（避免与 shared SQL 重复执行）。
// 启动期校验：open connection + information_schema.tables 验证 expected tables 存在。
//
// Npgsql 8 起 Dictionary<string,object?> ↔ jsonb 动态映射需显式 EnableDynamicJson()（不再默认开启）。
// 不开就报：Reading as 'Dictionary`2' is not supported for fields having DataTypeName 'jsonb'。
// 同样 ToSettingsDto 里 Str() / maxUsers switch 仍是必要的——System.Text.Json 反序列化原语值仍是 JsonElement。
var pgConn = builder.Configuration.GetConnectionString("Postgres");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(pgConn);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(dataSource, npg => npg.MigrationsHistoryTable("__ef_migrations_history")));

// v0.2.0 NSwag-generated Controllers + 11 concrete implementations
// Controllers 在 src/Controllers/Generated/Controllers.cs（NSwag 产物，勿手改）
// concrete 实现 在 src/Controllers/Implementation/<Tag>Controller.cs（手写业务）
builder.Services.AddControllers()
    .AddApplicationPart(typeof(Saas.Identity.AspNetCore.Controllers.Generated.AdminAppsControllerBase).Assembly);

// Swagger UI（与 springboot v0.1.13 springdoc-openapi-starter-webmvc-ui 对齐）：
// Swashbuckle 已是 csproj 依赖但未接线；现在 OpenAPI 文档在线暴露在 /swagger，
// 便于前端 orval 复核 / QA curl 试验端点。
// Title 用项目名 + stack 标识；版本从 assembly 取。
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "saas-identity-platform-aspnetcore",
        Version = "v1",
        Description = "ASP.NET Core 8 后端。NSwag 读 ../saas-identity-platform-shared/generated/openapi/openapi.yaml 产 Controllers.cs；concrete 实现见 src/Controllers/Implementation/。",
    });
});

builder.Services.AddScoped<AdminAppsController>();
builder.Services.AddScoped<AdminAppMenusController>();
builder.Services.AddScoped<AdminTenantsController>();
builder.Services.AddScoped<AuthController>();
builder.Services.AddScoped<MeController>();
builder.Services.AddScoped<OauthController>();
builder.Services.AddScoped<TenantApiKeysController>();
builder.Services.AddScoped<TenantAuditController>();
builder.Services.AddScoped<TenantRolesController>();
builder.Services.AddScoped<TenantRoleMenusController>();
builder.Services.AddScoped<TenantUsersController>();

var app = builder.Build();

// Swagger UI：dev/staging 在线暴露，prod 通过 ASPNETCORE_ENVIRONMENT 之外的条件控制（v0.1.5 暂全开）。
// 与 springboot springdoc-openapi 同位：契约文档是 SSOT，前端 orval 复核与 QA 联调都靠它。
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "saas-identity-platform-aspnetcore v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("NextDev");
app.UseAuthentication();
app.UseAuthorization();
// OAuth 端点的参数校验错误（INVALID_SCOPE / INVALID_REDIRECT_URI / ...）以 400 JSON 返回，
// 而不是 ASP.NET 默认的 500 空 body —— lab 后端 EnsureSuccessStatusCode 只能看到裸 500，
// 排障时无从区分（曾因此把 scope 不匹配当成网络/DB 故障查了一轮）。
// UnauthorizedAccessException（INVALID_CLIENT / INVALID_GRANT）→ 401，带同样的 JSON body。
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async ctx =>
    {
        var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        ctx.Response.StatusCode = ex switch
        {
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            ArgumentException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        };
        ctx.Response.ContentType = "application/json";
        var code = ex switch
        {
            UnauthorizedAccessException => "UNAUTHORIZED",
            ArgumentException => "INVALID_REQUEST",
            _ => "INTERNAL_ERROR",
        };
        await ctx.Response.WriteAsJsonAsync(new { error = code, error_description = ex?.Message ?? "unknown" });
    });
});
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }