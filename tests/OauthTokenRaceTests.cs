using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// 5.123①② 竞态机制钉（tracker：rails 先行定形状，aspnetcore 同形镜像）。
/// ② rotate 必须撤销旧行——修前 OauthController.RotateRefreshToken 只 Add 新行不删旧行
///   （注释声称 consumed 但无对应写入），rotation 后同一 refreshToken 复用恒 200；
/// ① code claim 影响行数判定——并发输家（删 0 行）必须 INVALID_GRANT 拒签；
///   InMemory provider 下并发交错不可达（读本环即断言 null），Npgsql 侧由
///   DbUpdateConcurrencyException→INVALID_GRANT 映射承接，此处钉顺序重放语义。
/// </summary>
public class OauthTokenRaceTests
{
    private const string ClientId = "saas-console";

    private static readonly Guid UserId = new("00000000-0000-0000-0000-00000000aa01");
    private static readonly Guid TenantId = new("00000000-0000-0000-0000-00000000bb01");

    private static (AppDbContext db, OauthController ctrl) Make(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName).Options;
        var db = new AppDbContext(options);
        // exchange 校验 user 绑定（"code user not in saas"），种子用户须在库
        db.SysUsers.Add(new Infrastructure.Persistence.Generated.SysUser
        {
            Id = UserId,
            Username = "race-alice",
            Password = "n/a",
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        var jwt = new JwtIssuer(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SIGNING_KEY"] = "unit-test-signing-key-0123456789abcdef",
                ["JWT_ISSUER"] = "saas-test",
                ["JWT_AUDIENCE"] = "saas-test",
            }).Build());
        var ctrl = new OauthController(db, jwt)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (db, ctrl);
    }

    private static OauthClient Client() => new()
    {
        Id = Guid.NewGuid(),
        ClientId = ClientId,
        ClientSecret = "n/a",
        ClientName = "race-test",
        GrantTypes = "authorization_code,refresh_token",
        RedirectUris = "http://localhost/callback",
        Scopes = "read,write",
        AccessTokenValidity = 3600,
        RefreshTokenValidity = 604800,
        Status = 1,
        CreatedAt = DateTime.UtcNow,
    };

    private static OauthCode RefreshRow(string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        ClientId = ClientId,
        UserId = UserId,
        TenantId = TenantId,
        Scope = "read",
        ExpiresAt = DateTime.UtcNow.AddDays(1),
        CreatedAt = DateTime.UtcNow,
    };

    private static TokenRequest RefreshReq(string rt) => new()
    {
        GrantType = TokenRequestGrantType.Refresh_token,
        ClientId = ClientId,
        RefreshToken = rt,
    };

    [Fact]
    [Trait("Fn", "M04.F03.I03")]
    public async Task Rotate_revokesOldRefresh_reuseIsRejected()
    {
        var (db, ctrl) = Make("oauth-race-rotate");
        db.OauthClients.Add(Client());
        db.OauthCodes.Add(RefreshRow("rt-old"));
        await db.SaveChangesAsync();

        var first = await ctrl.Token(RefreshReq("rt-old"));
        Assert.False(string.IsNullOrEmpty(first.AccessToken));

        // 旧实现：rotate 只 Add 新行不撤旧行 → 复用恒 200（半态家族错位）
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => ctrl.Token(RefreshReq("rt-old")));
        Assert.Contains("INVALID_GRANT", ex.Message);
    }

    [Fact]
    [Trait("Fn", "M04.F03.I02")]
    public async Task Exchange_replayAfterClaimIsRejected_andLoserIssuesNothing()
    {
        var (db, ctrl) = Make("oauth-race-exchange");
        db.OauthClients.Add(Client());
        db.OauthCodes.Add(new OauthCode
        {
            Id = Guid.NewGuid(),
            Code = "ac-once",
            ClientId = ClientId,
            UserId = UserId,
            TenantId = TenantId,
            RedirectUri = "http://localhost/callback",
            Scope = "read",
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var pairsBefore = await db.OauthCodes.CountAsync();

        var req = new TokenRequest
        {
            GrantType = TokenRequestGrantType.Authorization_code,
            ClientId = ClientId,
            Code = "ac-once",
            RedirectUri = "http://localhost/callback",
        };
        var first = await ctrl.Token(req);
        Assert.False(string.IsNullOrEmpty(first.AccessToken));

        // 重放 = claim 已被赢家消费（deleted=0 形态的顺序投影）→ INVALID_GRANT 拒签
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => ctrl.Token(req));
        Assert.Contains("INVALID_GRANT", ex.Message);
        // exchange 删 1 加 1 行数守恒；重放不得再签新行
        Assert.Equal(pairsBefore, await db.OauthCodes.CountAsync());
    }
}
