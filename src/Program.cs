using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Services;
using Saas.Identity.AspNetCore.Controllers.Implementation;

// 2026-08-30 fail-fast：secret 缺失立即抛错，并指明缺哪个 key、去哪配。
// dev 用 .env.test（带 dev-key-32-bytes-minimum-length!）；prod 用 VPS env-file 注入。
// secret 长度 ≥32B 是 HS256 RFC 7518 硬约束（<32B 启动时直接抛）。
static SymmetricSecurityKey BuildJwtSigningKey(IConfiguration cfg)
{
    var key = cfg["JWT_SIGNING_KEY"];
    if (string.IsNullOrEmpty(key))
    {
        throw new InvalidOperationException(
            "JWT_SIGNING_KEY 未配置。dev 加载 .env.test；prod 由 deploy 脚本写入 VPS env-file。" +
            "本规则遵循 CLAUDE.md「禁止 env 默认值兜底」：secret 缺失必须 fail-fast。");
    }
    var bytes = System.Text.Encoding.UTF8.GetByteCount(key);
    if (bytes < 32)
    {
        throw new InvalidOperationException(
            $"JWT_SIGNING_KEY 长度不足 32B（HS256 RFC 7518 要求），当前 {bytes}B。");
    }
    return new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(key));
}

// appsettings*.json 在仓根，csproj 已拷到 bin/。强制 ContentRoot = bin 目录，
// 这样不管 cwd 是 src/、仓根、还是生产部署的任意路径，配置文件都能被加载。
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// conventions §6: 家族统一监听 key SERVER_PORT（aspnetcore=5104）。ASPNETCORE_URLS
// 优先级更高（容器内 Dockerfile ENV 已设）, 本 shim 只服务裸机 dotnet run。
var shimUrls = Saas.Identity.AspNetCore.Hosting.ServerPortShim.ResolveUrls(builder.Configuration);
if (shimUrls is not null)
{
    builder.WebHost.UseUrls(shimUrls);
}

// JWT bearer auth — tenant_id claim is mandatory for tenant-scoped routes
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Phase 4 env 对称化: env var 走 flat JWT_SIGNING_KEY/JWT_ISSUER/JWT_AUDIENCE (与 saas-springboot + saas-msw 镜像)
        // ASP.NET Core 默认 env provider 直接把 flat key 当 flat config 读, 不走 `:` 段映射
        // (`:` 段会变成 `__` 双下划线)。所以这里读 flat key, 与其他 6 仓命名对齐。
        options.Authority = builder.Configuration["JWT_AUTHORITY"];
        // 2026-08-29 修 saas-vue → saas-aspnetcore /api/v1/oauth/authorize Bearer
        // token fallback 401: JwtBearer 默认 MapInboundClaims=true,把 JWT 'sub' 映射
        // 到 ClaimTypes.NameIdentifier (= http://schemas.xmlsoap.org/ws/2005/05/
        // identity/claims/nameidentifier)。OAuthController.Authorize 用 User.FindFirstValue
        // ('sub') / ('tenant_id') 读 claim,默认配置下 'sub' 找不到 → fallback 失败。
        // 关 MapInboundClaims 后,claim 名原样保留 'sub' / 'tenant_id' (与 JwtIssuer
        // 写的名字一致,RFC 7519 标准命名)。
        options.MapInboundClaims = false;
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
            // 2026-08-30 fail-fast：删掉 `?? "dev-key-32-bytes-minimum-length!"` 静默兜底。
            // secret 缺失必须显式失败，并指出缺哪个 key、去哪配（CLAUDE.md 硬规则）。
            IssuerSigningKey = BuildJwtSigningKey(builder.Configuration),
        };
    });

// CORS — 允许跨 origin 调本后端的白名单。dev 期 localhost 列表,
// 生产用 SAAS_CORS_ALLOWED_ORIGINS env override（逗号分隔）改正式域名。
// 与 springboot 端的 SecurityConfig.corsConfigurationSource() 对称 — 同一 env var。
//
// ADR-0019：缺失 throw，不允许 fallback 到 localhost dev 列表（生产误部署会让任何
// localhost origin 调本后端 OAuth,等同于 OAuth CORS 失效）。
builder.Services.AddCors(options =>
{
    var originsValue = builder.Configuration["SAAS_CORS_ALLOWED_ORIGINS"];
    if (string.IsNullOrEmpty(originsValue))
    {
        throw new InvalidOperationException(
            "SAAS_CORS_ALLOWED_ORIGINS env is required (ADR-0019 禁 localhost 兜底). "
            + "Set comma-separated origins in .env.local (dev) or env (prod).");
    }
    var origins = originsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    options.AddPolicy("NextDev", policy =>
        policy.WithOrigins(origins)
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
// M03.F01 (ADR-0013 路线 A)：session 三件套。进程内存储 — AuthController.Login 写
// cookie，SaasSessionMiddleware 读 store 注入 Items，Oauth/Me 控制器消费。
// singleton：session 必须全进程共享；Phase 6+ 多副本部署切 Redis（ADR-0014）。
builder.Services.AddSingleton<SaasSessionStore>();
// M03.F01.I02 失败锁定（5 次 / 15min），进程内计数 — 同上 singleton。
builder.Services.AddSingleton<FailedLoginStore>();

// M10.Database — EF Core + Npgsql + snake_case 命名（ADR-0010）
// shared SQL 是 SSOT；EF Model 镜像；启动时**不调** Database.Migrate()（避免与 shared SQL 重复执行）。
// 启动期校验：open connection + information_schema.tables 验证 expected tables 存在。
//
// Npgsql 8 起 Dictionary<string,object?> ↔ jsonb 动态映射需显式 EnableDynamicJson()（不再默认开启）。
// 不开就报：Reading as 'Dictionary`2' is not supported for fields having DataTypeName 'jsonb'。
// 同样 ToSettingsDto 里 Str() / maxUsers switch 仍是必要的——System.Text.Json 反序列化原语值仍是 JsonElement。
// DATABASE_URL 是全家族统一 key（2026-08-28 接线；deploy 脚本只写它，不再依赖
// appsettings.json 内嵌的 dev 连接串）。ConnectionStrings:Postgres 仍作 fallback。
var pgConn = builder.Configuration["DATABASE_URL"]
    ?? builder.Configuration.GetConnectionString("Postgres");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(pgConn);
dataSourceBuilder.EnableDynamicJson();
// M05.F01 / M06 PG native enum 映射（V004 api_key_status / V006 audit_action）——
// 不绑 MapEnum 则 Npgsql 把字符串当 text 发，PG 报 42804 column is of type enum。
// EF Core 端写 string（`DbApiKey.Status = "active"`），读写双向 Npgsql 都做 enum↔string。
dataSourceBuilder.MapEnum<ApiKeyStatusPg>("api_key_status");
dataSourceBuilder.MapEnum<AuditActionPg>("audit_action");
dataSourceBuilder.MapEnum<UserStatusPg>("user_status");
dataSourceBuilder.MapEnum<MembershipStatusPg>("membership_status");
// M00.F01 tenant_status（V001）——2026-08-31 contract-test M96.F02.I30 同款 42804 修复
dataSourceBuilder.MapEnum<TenantStatusPg>("tenant_status");
// M07/M08（V005）——2026-09-01 contract-test I45/I51 同款 42804 修复：
// app_status / menu_status / menu_type / oauth_grant_type（含 enum 数组 grant_types）
dataSourceBuilder.MapEnum<AppStatusPg>("app_status");
dataSourceBuilder.MapEnum<MenuStatusPg>("menu_status");
dataSourceBuilder.MapEnum<MenuTypePg>("menu_type");
dataSourceBuilder.MapEnum<OAuthGrantTypePg>("oauth_grant_type");
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(dataSource, npg => npg.MigrationsHistoryTable("__ef_migrations_history")));

// v0.2.0 NSwag-generated Controllers + 11 concrete implementations
// Controllers 在 src/Controllers/Generated/Controllers.cs（NSwag 产物，勿手改）
// concrete 实现 在 src/Controllers/Implementation/<Tag>Controller.cs（手写业务）
builder.Services.AddControllers()
    .AddApplicationPart(typeof(Saas.Identity.AspNetCore.Controllers.Generated.AdminAppsControllerBase).Assembly)
    // 2026-08-30：合同测试发现 aspnetcore enum 序列化为 PascalCase（"Active"），
    // OpenAPI/TypeSpec 与 msw/nextjs/springboot 都期望小写（"active"）。
    //
    // .NET 8 全局 JsonStringEnumConverter 被属性级 [JsonStringEnumConverter] 覆盖
    // （按 STJ 文档：属性级 converter 优先级 > 全局 Converters 集合）。
    // 因此用 TypeInfoResolver.Modifier 把 enum 字段的 CustomConverter 显式设为
    // SnakeCaseLower —— CustomConverter 在 STJ 解析路径上 > JsonConverterAttribute，
    // 真正能盖住 NSwag 注入的属性级 converter。
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(
                System.Text.Json.JsonNamingPolicy.SnakeCaseLower));
        var resolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)
                return;
            foreach (var prop in typeInfo.Properties)
            {
                if (System.Nullable.GetUnderlyingType(prop.PropertyType) is { } u && u.IsEnum)
                    prop.CustomConverter = new System.Text.Json.Serialization.JsonStringEnumConverter(
                        System.Text.Json.JsonNamingPolicy.SnakeCaseLower);
                else if (prop.PropertyType.IsEnum)
                    prop.CustomConverter = new System.Text.Json.Serialization.JsonStringEnumConverter(
                        System.Text.Json.JsonNamingPolicy.SnakeCaseLower);
            }
        });
        o.JsonSerializerOptions.TypeInfoResolver = resolver;
    });

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
// M06.F03.I01 审计写入助手 —— 所有写端点副作用发 audit_events。
// Scoped：与 EF DbContext 同生命周期，单请求内可共享事务上下文。
builder.Services.AddScoped<IAuditWriter, AuditWriter>();
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
// M03.F01.I01 — saas session cookie 解析（注册位置见 SaasSessionMiddleware 注释）。
// 必须在 UseAuthentication 之前：OAuth 端点读 Items["saasSession"] 判登录态。
app.UseMiddleware<SaasSessionMiddleware>();
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
            AccountLockedException => StatusCodes.Status423Locked,
            ArgumentException => StatusCodes.Status400BadRequest,
            // 2026-08-31 contract-test I21：M05.F01.I05 物理删幂等 — 重复 DELETE 已不存在的 keyId
            // 抛 KeyNotFoundException → 404（真后端要返 404，Next.js/m-sw/orval oracle 已对齐）。
            KeyNotFoundException => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError,
        };
        ctx.Response.ContentType = "application/json";
        var code = ex switch
        {
            UnauthorizedAccessException => "UNAUTHORIZED",
            AccountLockedException => "ACCOUNT_LOCKED",
            ArgumentException => "INVALID_REQUEST",
            KeyNotFoundException => "NOT_FOUND",
            _ => "INTERNAL_ERROR",
        };
        await ctx.Response.WriteAsJsonAsync(new { error = code, error_description = ex?.Message ?? "unknown" });
    });
});
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }