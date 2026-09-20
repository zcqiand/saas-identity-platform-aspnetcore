using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Infrastructure.Audit;
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
// M01.F04.I02 失败锁定（5 次 / 15min），进程内计数 — 同上 singleton。
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
// 2026-09-12 live 跑批 53300 修复：Npgsql 默认 Max Pool Size=100，四方 contract-test
// 全量并发跑批时把共享 PG（max_connections=100，同实例还住着 lab/saas_prod 等 4 个库）
// 打满 → "sorry, too many clients already" → login 500 级联（59 failed 的主因）。
// 显式压到 10，与 springboot Hikari / nextjs pg-pool 的默认档对齐——多后端共库是
// 「前端不可区分」的物理基础，单后端不得独占连接预算。
var dataSourceBuilder = new NpgsqlDataSourceBuilder(
    new NpgsqlConnectionStringBuilder(pgConn) { MaxPoolSize = 10 }.ConnectionString);
dataSourceBuilder.EnableDynamicJson();
// v0.5.0：所有 PG native enum 已废（api_key_status / audit_action / user_status /
// membership_status / tenant_status / app_status / menu_status / menu_type /
// oauth_grant_type），对应表已 DROP 或 status 列改 smallint（9/7 重组）。
// Scaffold DbContext 自动按 smallint 读写，无需 MapEnum。
// 若后续需要重新引入 PG native enum，对应 entity 也需 Domain/Entities/*.cs 复活
// —— 目前 Generated/* 都是 smallint 映射。
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(dataSource));

// v0.2.0 NSwag-generated Controllers + 11 concrete implementations
// Controllers 在 src/Controllers/Generated/<Tag>Controller.cs（NSwag 产物，§2.2 按 tag 拆，勿手改）
// DTOs 在 src/Models/Generated/<Name>.cs（同上，split 自 AllGenerated.cs）
// concrete 实现 在 src/Controllers/Implementation/<Tag>Controller.cs（手写业务）
builder.Services.AddControllers(o =>
{
    // 2026-09-12 修复：body 反序列化失败（未知枚举串 type:"page"、坏 JSON 等）时 JsonInputFormatter
    // 只记 ModelState error、[FromBody] 参数（BindRequired）留 null —— 本仓控制器无 [ApiController]
    // 自动 400，action 继续跑 → body.ParentId NullReferenceException → 500。
    // 此 filter：ModelState invalid 且有 body 参数没绑上 → 400 INVALID_REQUEST；
    // 属性级 [Required] 校验失败时参数对象已构造（非 null）不受影响，保持既有行为。
    o.Filters.Add<MalformedBodyFilter>();
    // 5.64 契约注解运行时 enforce（人裁 2026-09-20）：DTO 的 [Required]/[StringLength]
    // 此前只进 ModelState、无人消费 → 短密码（契约 @minLength(8)）真 200 建行。
    // 此 filter 只拦 body DTO 属性级校验失败（详见 ModelStateValidationFilter 注释）；
    // 注册在 MalformedBodyFilter 之后 —— body 绑定失败（参数被剔除）由前者短路，
    // 两 filter 职责边界见各自类头注释。
    o.Filters.Add<ModelStateValidationFilter>();
    // 5.34 错误语义收口：关掉 MVC 对 NRT 非空引用属性的隐式 [Required] 推断
    //（lab 先例 lab-management-system-aspnetcore/src/Program.cs 同款一行修）；
    // 契约必填校验由 NSwag 产出的显式 [Required] 承担，不受此开关影响。
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
})
    .AddApplicationPart(typeof(Saas.Identity.AspNetCore.Controllers.Generated.ClientMenusControllerBase).Assembly)
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
        // 2026-09-12 修复：enum 属性必须走非泛型 JsonStringEnumConverter(SnakeCaseLower)。
        // NSwag 属性级 [JsonStringEnumConverter<SysMenuType>] 是 .NET 8 泛型版，两个问题：
        // ① 本 runtime (8.0.x) 泛型版忽略 EnumMemberAttribute → 序列化出 PascalCase（"Active"），
        //    与 msw/nextjs/springboot 的小写 "active" 分叉；
        // ② 泛型版读未知枚举串（如 type:"page"）抛 NullReferenceException 而非 JsonException
        //    → 绕过输入格式化器的 400 路径，冒 500。
        // SnakeCaseLower 命名与全部 EnumMember 值一一对应（active/invited/menu/authorization_code...），
        // 非 generic 版未知值抛 JsonException → JsonInputFormatter 正常 400。
        // CustomConverter 优先级 > JsonConverterAttribute（STJ 文档），能真正盖住 NSwag 属性级 converter。
        var resolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)
                return;
            foreach (var prop in typeInfo.Properties)
            {
                var enumType = System.Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (enumType.IsEnum)
                    // 2026-09-20 5.34 修复：CustomConverter 必须显式支持 Nullable<enum> —— 非泛型
                    // JsonStringEnumConverter.CanConvert 只认 enum 本体，对 SysUserStatus? 这类
                    // 可空枚举属性直接抛 "not supported by the current JsonConverterFactory" →
                    // body 参数被剔除 → MalformedBodyFilter 400（live 对拍 I39/I70 家族根因）。
                    prop.CustomConverter = new SnakeCaseEnumJsonConverterFactory();
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
        Description = "ASP.NET Core 8 后端。NSwag 读 ../saas-identity-platform-shared/generated/openapi/openapi.yaml 产 AllGenerated.cs → 按类拆分为 src/Controllers/Generated/<Tag>Controller.cs + src/Models/Generated/<Dto>.cs（spec §2.2）；concrete 实现见 src/Controllers/Implementation/。",
    });
});

// 9/7 重组：AdminAppsController / TenantApiKeysController / TenantAuditController 已废
// （对应 admin_apps / api_keys / audit_events 表 DROP，路径不再生成）。DI 注册一并移除。
builder.Services.AddScoped<ClientMenusController>();
builder.Services.AddScoped<ClientsController>();
builder.Services.AddScoped<AdminTenantsController>();
builder.Services.AddScoped<AdminClientsController>();
builder.Services.AddScoped<AuthController>();
builder.Services.AddScoped<MeController>();
builder.Services.AddScoped<OauthController>();
builder.Services.AddScoped<TenantRolesController>();
builder.Services.AddScoped<TenantRoleMenusController>();
builder.Services.AddScoped<IAuditWriter, AuditWriter>();
builder.Services.AddScoped<TenantMembersController>();
builder.Services.AddScoped<TenantApplicationsController>();

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
// M01.F04.I03 — saas session cookie 解析（注册位置见 SaasSessionMiddleware 注释）。
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
        // M01.F04.I02 — 失败锁定走 shared LockedAccountResponse 契约：
        // { code: "ACCOUNT_LOCKED", message, lockedUntil: ISO-8601, remainingAttempts? }。
        // 其它错误保留旧 { error, error_description } 形状（pre-existing，非本次任务范围）。
        if (ex is AccountLockedException lockEx)
        {
            await ctx.Response.WriteAsJsonAsync(new
            {
                code,
                message = lockEx.Message,
                lockedUntil = lockEx.UnlockAt.ToString("O"),
            });
        }
        else
        {
            await ctx.Response.WriteAsJsonAsync(new { error = code, error_description = ex?.Message ?? "unknown" });
        }
    });
});
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>body JSON 反序列化失败（参数没绑上）→ 400 INVALID_REQUEST，不让 null body 冒 500。</summary>
internal sealed class MalformedBodyFilter : Microsoft.AspNetCore.Mvc.Filters.IActionFilter
{
    public void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        // 只管 [FromBody] 参数：MVC 把绑定失败的 body **直接从 ActionArguments 剔除**（不是放 null）。
        // query 参数（clientId 等）绑定失败不拦 —— 实现层有空值兜底（不过滤），保持既有宽松行为。
        if (context.ActionDescriptor.Parameters.Any(p =>
                p.BindingInfo?.BindingSource == Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Body
                && !context.ActionArguments.ContainsKey(p.Name)))
        {
            var message = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault(m => !string.IsNullOrEmpty(m)) ?? "invalid request body";
            context.Result = new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
            {
                error = "INVALID_REQUEST",
                error_description = message,
            });
        }
    }

    public void OnActionExecuted(Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext context) { }
}

/// <summary>
/// 5.64（2026-09-20 人裁）：DTO DataAnnotations 校验失败 → 400 INVALID_REQUEST。
/// 本仓无 [ApiController]（禁改，那会动推断/绑定行为面），NSwag 产物的
/// [Required]/[StringLength] 只记 ModelState、无 filter 消费 → 契约长度/必填运行时不 enforce。
///
/// 职责边界（与 MalformedBodyFilter 互补，勿合并）：
///   - MalformedBodyFilter：body 反序列化失败 —— 参数对象没构造出来，被从 ActionArguments 剔除 → 400。
///   - 本 filter：body 参数已绑定成功，但属性级校验（[Required]/[StringLength] 等）失败 → 400。
///     只收 ModelState 里归属 body DTO 的条目（空前缀下 key = DTO 属性名，或「body参数名.」前缀）；
///     query/route 参数的绑定失败/缺省产生的错误（key = 参数名）不拦 —— 保持既有宽松语义
///     （实现层有空值兜底，见 MalformedBodyFilter 同款约定）。
///
/// envelope 照抄 MalformedBodyFilter 的 400 形状 { error, error_description }（家族 error 两派
/// {code,message} vs {error,error_description}，本仓从后者），error_description = 校验错误汇总。
/// </summary>
internal sealed class ModelStateValidationFilter : Microsoft.AspNetCore.Mvc.Filters.IActionFilter
{
    public void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
            return;

        // 无 body 参数的 action（以及 body 没绑上的 —— 那是 MalformedBodyFilter 的辖域，
        // 且它注册在前、短路后本 filter 不会执行）不拦。
        var bodyParam = context.ActionDescriptor.Parameters.FirstOrDefault(p =>
            p.BindingInfo?.BindingSource == Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Body);
        if (bodyParam == null || !context.ActionArguments.ContainsKey(bodyParam.Name))
            return;

        // 错误条目必须归属 body DTO：body 复杂模型空前缀绑定，key = 属性名（"Password"）；
        // 兜底带前缀形态（"body.Password"）。query/route 错误的 key 是参数名，天然不匹配。
        var propNames = bodyParam.ParameterType.GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prefix = bodyParam.Name + ".";

        var errors = context.ModelState
            .Where(kv => kv.Key.Length == 0
                || propNames.Contains(kv.Key)
                || kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value!.Errors) // ModelStateDictionary 枚举 Value 标注可空，实际非空
            .ToList();
        if (errors.Count == 0)
            return;

        var message = string.Join("; ", errors
            .Select(e => !string.IsNullOrEmpty(e.ErrorMessage) ? e.ErrorMessage : e.Exception?.Message)
            .Where(m => !string.IsNullOrEmpty(m)));
        if (string.IsNullOrEmpty(message))
            message = "invalid request body";

        context.Result = new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
        {
            error = "INVALID_REQUEST",
            error_description = message,
        });
    }

    public void OnActionExecuted(Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext context) { }
}

public partial class Program { }

/// <summary>
/// 5.34 错误语义收口（2026-09-20）：SnakeCaseLower 枚举绑定 converter，nullable-aware。
/// 非泛型 JsonStringEnumConverter.CanConvert 只接受 enum 本体；NSwag 生成的请求 DTO 里
/// 枚举普遍是 SysUserStatus? 这类可空形态，直接挂上会在首次反序列化时抛
/// "not supported by the current JsonConverterFactory" → JsonInputFormatter 记 binding 失败 →
/// body 参数被剔除 → MalformedBodyFilter 400 INVALID_REQUEST，请求到不了控制器。
/// 这里对 Nullable&lt;TEnum&gt; 包一层解包 converter，序列化/反序列化语义与非可空属性完全一致
///（snake_case 小写，与非泛型 converter + SnakeCaseLower 现行为对齐）。
/// </summary>
internal sealed class SnakeCaseEnumJsonConverterFactory : System.Text.Json.Serialization.JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsEnum ||
        System.Nullable.GetUnderlyingType(typeToConvert)?.IsEnum == true;

    public override System.Text.Json.Serialization.JsonConverter? CreateConverter(
        Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        // STJ 规定 factory 的 CreateConverter 必须返回具体 converter（不能再是 factory），
        // 所以非可空分支也要把 JsonStringEnumConverter 展开到 concrete enum converter。
        var factory = new System.Text.Json.Serialization.JsonStringEnumConverter(
            System.Text.Json.JsonNamingPolicy.SnakeCaseLower);
        var enumType = System.Nullable.GetUnderlyingType(typeToConvert);
        if (enumType is null)
            return factory.CreateConverter(typeToConvert, options);
        var wrapper = typeof(NullableEnumJsonConverter<>).MakeGenericType(enumType);
        return (System.Text.Json.Serialization.JsonConverter?)Activator.CreateInstance(wrapper, options);
    }

    /// <summary>Nullable&lt;TEnum&gt; 解包：null 短路，非 null 委托给非可空 enum converter。</summary>
    private sealed class NullableEnumJsonConverter<T> : System.Text.Json.Serialization.JsonConverter<T?>
        where T : struct
    {
        private readonly System.Text.Json.Serialization.JsonConverter<T> _inner;

        public NullableEnumJsonConverter(System.Text.Json.JsonSerializerOptions options)
        {
            _inner = (System.Text.Json.Serialization.JsonConverter<T>)new System.Text.Json.Serialization.JsonStringEnumConverter(
                    System.Text.Json.JsonNamingPolicy.SnakeCaseLower)
                .CreateConverter(typeof(T), options)!;
        }

        public override T? Read(
            ref System.Text.Json.Utf8JsonReader reader,
            Type typeToConvert,
            System.Text.Json.JsonSerializerOptions options)
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.Null) return null;
            return _inner.Read(ref reader, typeof(T), options);
        }

        public override void Write(
            System.Text.Json.Utf8JsonWriter writer,
            T? value,
            System.Text.Json.JsonSerializerOptions options)
        {
            if (value.HasValue) _inner.Write(writer, value.Value, options);
            else writer.WriteNullValue();
        }
    }
}