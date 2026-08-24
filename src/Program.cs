using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
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
        options.Authority = builder.Configuration["Jwt:Authority"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"] ?? "dev-key-32-bytes-minimum-length!")),
        };

        // Dev only：next.js 端 MSW/dev-helper 发的是 alg=none + .dev-placeholder 的模拟 token
        // （见 Authorization header 解码：{"alg":"none"}.{...}.dev-placeholder）。
        // 标准 JwtBearer 8 默认拒收 alg=none（安全硬编码）；dev 必须显式放行。
        // 关键开关：RequireSignedTokens=false（放行 unsigned）+ ValidateIssuerSigningKey=false（不查 key）。
        // 切 legacy JwtSecurityTokenHandler + SignatureValidator 是 belt-and-suspenders 双保险。
        // Production 走真实对称 key 标准流程（上面的 TokenValidationParameters）。
        if (builder.Environment.IsDevelopment())
        {
            options.UseSecurityTokenValidators = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = false,
                RequireSignedTokens = false,                                       // ← 真正接受 alg=none 的开关
                SignatureValidator = (token, _) => new JwtSecurityToken(token),
            };
        }
    });

// CORS — 允许跨 origin 调本后端的白名单。默认给 dev：
//   - saas-nextjs :3000（saas 全栈 Next.js API routes 调 saas 后端）
//   - lab-react,vue :5173（lab 前端 dev server 调 saas 后端走 MSW switch）
//   - lab-nextjs :3001（lab 全栈 Next.js API routes 调 saas 后端）
// 生产用 SAAS_CORS_ALLOWED_ORIGINS env override（逗号分隔）改正式域名。
// 与 springboot 端的 SecurityConfig.corsConfigurationSource() 对称 — 同一 env var。
builder.Services.AddCors(options =>
{
    var origins = builder.Configuration["Saas:Cors:AllowedOrigins"]
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
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }