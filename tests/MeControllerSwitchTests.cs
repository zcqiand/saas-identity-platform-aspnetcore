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
    private static (AppDbContext db, JwtIssuer jwt) MakeDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name).Options;
        var db = new AppDbContext(options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JWT_SIGNING_KEY"] = "test-signing-key-0123456789abcdef0123456789",
            ["JWT_ISSUER"] = "test-issuer",
            ["JWT_AUDIENCE"] = "test-audience",
        }).Build();
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
}
