using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// M01.F03.I02 — 切换当前租户：Switch 必须签发 HS256 真 token（ADR-0019 审计红线 #4，
/// 旧实现返回 alg=none dev-placeholder，refresh token 也是 refresh-{uid}-{ts} 假货）。
/// </summary>
public class MeControllerSwitchTests
{
    private static (AppDbContext db, JwtIssuer jwt) MakeDb(string name, int? ttlSeconds = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name).Options;
        var db = new AppDbContext(options);
        var configPairs = new Dictionary<string, string?>
        {
            ["JWT_SIGNING_KEY"] = "test-signing-key-0123456789abcdef0123456789",
            ["JWT_ISSUER"] = "test-issuer",
            ["JWT_AUDIENCE"] = "test-audience",
        };
        if (ttlSeconds is not null)
            configPairs["JWT_TTL_SECONDS"] = ttlSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var config = new ConfigurationBuilder().AddInMemoryCollection(configPairs).Build();
        return (db, new JwtIssuer(config));
    }

    [Fact]
    [Trait("Fn", "M01.F03.I02")]
    public async Task Switch_returnsHs256Token_notAlgNone()
    {
        var (db, jwt) = MakeDb("switch-hs256");
        var uid = Guid.NewGuid();
        var tid = Guid.NewGuid();
        // 必填字段以 Generated entity 为准：SysUser 必填 Username/Password（无 DisplayName 列）
        db.SysUsers.Add(new SysUser
        {
            Id = uid,
            Username = "u",
            Password = "pw",
            Email = "u@t.cn",
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        db.TenantMembers.Add(new TenantMember
        {
            Id = Guid.NewGuid(),
            UserId = uid,
            TenantId = tid,
            MemberName = "u",
            IsOwner = false,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            { new(ClaimTypes.NameIdentifier, uid.ToString()) }));

        var ctrl = new MeController(db, http, jwt);
        var resp = await ctrl.Switch(tid.ToString(), "lab-management");

        // HS256 JWT：3 段、header alg == HS256（不是 none）
        var parts = resp.AccessToken.Split('.');
        Assert.Equal(3, parts.Length);
        var jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(resp.AccessToken);
        Assert.Equal("HS256", jwtToken.Header.Alg);
        // claims 带 sub + tenant_id（切到目标租户）
        Assert.Equal(uid.ToString(), jwtToken.Subject);
        Assert.Equal(tid.ToString(), jwtToken.Claims.First(c => c.Type == "tenant_id").Value);
        // refresh token 走 JwtIssuer.GenerateRefreshToken（saas-rt- 前缀，非 refresh- 假货）
        Assert.StartsWith("saas-rt-", resp.RefreshToken);
    }

    /// <summary>
    /// ExpiresAt 必须来自 JwtIssuer 的 JWT_TTL_SECONDS 配置（可配），
    /// 不是硬编码 AddHours(1)——短 TTL 配置下 ExpiresAt 应距 now ≤ TTL+缓冲。
    /// </summary>
    [Fact]
    [Trait("Fn", "M01.F03.I02")]
    public async Task Switch_expiresAt_followsJwtTtlSeconds_notHardcodedHour()
    {
        var (db, jwt) = MakeDb("switch-ttl", ttlSeconds: 60);
        var uid = Guid.NewGuid();
        var tid = Guid.NewGuid();
        db.SysUsers.Add(new SysUser
        {
            Id = uid,
            Username = "u2",
            Password = "pw",
            Email = "u2@t.cn",
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        db.TenantMembers.Add(new TenantMember
        {
            Id = Guid.NewGuid(),
            UserId = uid,
            TenantId = tid,
            MemberName = "u2",
            IsOwner = false,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            { new(ClaimTypes.NameIdentifier, uid.ToString()) }));

        var ctrl = new MeController(db, http, jwt);
        var before = DateTimeOffset.UtcNow;
        var resp = await ctrl.Switch(tid.ToString(), "lab-management");

        // JWT_TTL_SECONDS=60 → ExpiresAt 距 before 不超过 70s（60 + 10s 缓冲）；
        // 硬编码 AddHours(1) 会是 ~3600s，必红
        Assert.True(resp.ExpiresAt - before <= TimeSpan.FromSeconds(70),
            $"ExpiresAt should follow JWT_TTL_SECONDS=60, got delta={(resp.ExpiresAt - before).TotalSeconds:F0}s");
    }
}
