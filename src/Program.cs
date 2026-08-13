using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Controllers.Implementation;

var builder = WebApplication.CreateBuilder(args);

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
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TenantContext>();
builder.Services.AddSingleton<TenantGuard>();

// M10.Database — EF Core + Npgsql + snake_case 命名（ADR-0010）
// shared SQL 是 SSOT；EF Model 镜像；启动时**不调** Database.Migrate()（避免与 shared SQL 重复执行）。
// 启动期校验：open connection + information_schema.tables 验证 expected tables 存在。
var pgConn = builder.Configuration.GetConnectionString("Postgres");
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(pgConn, npg => npg.MigrationsHistoryTable("__ef_migrations_history")));

// v0.2.0 NSwag-generated Controllers + 11 concrete implementations
// Controllers 在 src/Controllers/Generated/Controllers.cs（NSwag 产物，勿手改）
// concrete 实现 在 src/Controllers/Implementation/<Tag>Controller.cs（手写业务）
builder.Services.AddControllers()
    .AddApplicationPart(typeof(Saas.Identity.AspNetCore.Controllers.Generated.AdminAppsControllerBase).Assembly);

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

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }