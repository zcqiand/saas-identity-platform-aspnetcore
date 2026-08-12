using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Service;

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
builder.Services.AddSingleton<TenantContext>();
builder.Services.AddSingleton<TenantGuard>();
builder.Services.AddSingleton<TenantUsersService>();
builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }