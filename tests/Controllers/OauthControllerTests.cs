using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Xunit;
using AppEntity = Saas.Identity.AspNetCore.Domain.Entities.App;
using UserEntity = Saas.Identity.AspNetCore.Domain.Entities.User;
using ApiKeyEntity = Saas.Identity.AspNetCore.Domain.Entities.ApiKey;
using AuditEventEntity = Saas.Identity.AspNetCore.Domain.Entities.AuditEvent;
using AuditRetentionPolicyEntity = Saas.Identity.AspNetCore.Domain.Entities.AuditRetentionPolicy;
using MenuEntity = Saas.Identity.AspNetCore.Domain.Entities.Menu;
using RoleMenuGrantEntity = Saas.Identity.AspNetCore.Domain.Entities.RoleMenuGrant;
using PermissionEntity = Saas.Identity.AspNetCore.Domain.Entities.Permission;
using RoleEntity = Saas.Identity.AspNetCore.Domain.Entities.Role;
using RolePermissionEntity = Saas.Identity.AspNetCore.Domain.Entities.RolePermission;
using TenantEntity = Saas.Identity.AspNetCore.Domain.Entities.Tenant;
using TenantMembershipEntity = Saas.Identity.AspNetCore.Domain.Entities.TenantMembership;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// 测试用 AppDbContext —— 跳过 PG 专属特性（jsonb / native enum），
/// 让 EF Core InMemory provider 能建 model。OAuth 测试只关心 Apps + Users + OauthCodes 三张表，
/// 其他 8 个 entity 直接 Ignore。
/// </summary>
internal class TestAppDbContext : AppDbContext
{
    public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // InMemory 不支持 Dictionary<string, object> (jsonb) / native enum
        modelBuilder.Ignore<ApiKeyEntity>();
        modelBuilder.Ignore<AuditEventEntity>();
        modelBuilder.Ignore<AuditRetentionPolicyEntity>();
        modelBuilder.Ignore<MenuEntity>();
        modelBuilder.Ignore<RoleMenuGrantEntity>();
        modelBuilder.Ignore<PermissionEntity>();
        modelBuilder.Ignore<RoleEntity>();
        modelBuilder.Ignore<RolePermissionEntity>();
        modelBuilder.Ignore<TenantEntity>();
        modelBuilder.Ignore<TenantMembershipEntity>();
    }
}

/// <summary>
/// M04.F03 OAuth 授权码签发 + 令牌交换 / 刷新（公开端点）。
/// v0.2.0：Phase 6 真 OAuth —— EF Core InMemory provider 跑真 LINQ 查询 (apps + oauth_codes)，
/// 不依赖 PG Testcontainers（那是 M10.F04 的范畴）。
/// </summary>
public class OauthControllerTests
{
    private const string TestSigningKey = "test-key-32-bytes-minimum-length-xyz12345";
    private static readonly Guid TestTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TestAppId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestClientIdGuid = TestAppId;  // V014: client_id = app.id 固定 UUID

    private static (AppDbContext db, JwtIssuer jwt, OauthController controller) Build(
        Guid? tenantId = null,
        Guid? userId = null,
        List<string>? redirectUris = null,
        List<string>? scopes = null,
        bool appActive = true)
    {
        var tid = tenantId ?? TestTenantId;
        var uid = userId ?? Guid.NewGuid();

        var dbOpts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"oauth-test-{Guid.NewGuid()}")
            .Options;
        var db = new TestAppDbContext(dbOpts);

        var app = new AppEntity
        {
            Id = TestAppId,
            Code = "lab-management",
            Name = "lab-mgmt",
            Status = appActive ? "active" : "disabled",
            ClientId = TestClientIdGuid.ToString(),
            RedirectUris = redirectUris ?? new List<string> { "https://lab-vue.xiangru.uk/login" },
            Scopes = scopes ?? new List<string> { "lab.read", "lab.write" },
            GrantTypes = new List<string> { "authorization_code", "refresh_token" },
            IsFirstParty = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Apps.Add(app);
        db.Users.Add(new UserEntity
        {
            Id = uid,
            TenantId = tid,
            Username = "test-user",
            Email = "test@lab.local",
            PasswordHash = "plain:dev",
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = TestSigningKey,
                ["Jwt:Issuer"] = "saas-identity-platform",
                ["Jwt:Audience"] = "saas-identity-platform-clients",
                ["Jwt:TtlSeconds"] = "3600",
            })
            .Build();
        var jwt = new JwtIssuer(config);
        var controller = new OauthController(db, jwt);
        return (db, jwt, controller);
    }

    [Fact]
    [Trait("Fn", "M04.F03.I07")]
    public async Task Authorize_happyPath_returnsCode()
    {
        var (_, _, c) = Build();
        var res = await c.Authorize(new AuthorizeCodeRequest
        {
            ClientId = TestClientIdGuid,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "lab.read",
            State = "test-state",
            TenantId = TestTenantId,
        });
        Assert.StartsWith("saas-code-", res.Code);
        Assert.Equal("test-state", res.State);
    }

    [Fact]
    [Trait("Fn", "M04.F03.I07")]
    public async Task Authorize_invalidClient_throwsUnauthorized()
    {
        var (_, _, c) = Build();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Authorize(new AuthorizeCodeRequest
        {
            ClientId = Guid.NewGuid(),  // 不存在的 client
            RedirectUri = "https://lab-vue.xiangru.uk/login",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "lab.read",
            State = "x",
            TenantId = TestTenantId,
        }));
    }

    [Fact]
    [Trait("Fn", "M04.F03.I07")]
    public async Task Authorize_invalidRedirectUri_throws()
    {
        var (_, _, c) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() => c.Authorize(new AuthorizeCodeRequest
        {
            ClientId = TestClientIdGuid,
            RedirectUri = "https://evil.example.com/callback",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "lab.read",
            State = "x",
            TenantId = TestTenantId,
        }));
    }

    [Fact]
    [Trait("Fn", "M04.F03.I07")]
    public async Task Authorize_invalidScope_throws()
    {
        var (_, _, c) = Build();
        await Assert.ThrowsAsync<ArgumentException>(() => c.Authorize(new AuthorizeCodeRequest
        {
            ClientId = TestClientIdGuid,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "admin.delete",  // 不在 apps.scopes
            State = "x",
            TenantId = TestTenantId,
        }));
    }

    [Fact]
    [Trait("Fn", "M04.F03.I08")]
    public async Task Token_authorizationCode_happyPath()
    {
        var (db, _, c) = Build();
        var code = (await c.Authorize(new AuthorizeCodeRequest
        {
            ClientId = TestClientIdGuid,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "lab.read",
            State = "x",
            TenantId = TestTenantId,
        })).Code;

        var tokenRes = await c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Authorization_code,
            Code = code,
            ClientId = TestClientIdGuid,
            TenantId = TestTenantId,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
        });
        Assert.StartsWith("ey", tokenRes.AccessToken);  // JWT 风格
        Assert.StartsWith("saas-rt-", tokenRes.RefreshToken);
        Assert.Equal("Bearer", tokenRes.TokenType);
        Assert.Equal("lab.read", tokenRes.Scope);
    }

    [Fact]
    [Trait("Fn", "M04.F03.I08")]
    public async Task Token_alreadyConsumedCode_throws()
    {
        var (db, _, c) = Build();
        var code = (await c.Authorize(new AuthorizeCodeRequest
        {
            ClientId = TestClientIdGuid,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "lab.read",
            State = "x",
            TenantId = TestTenantId,
        })).Code;

        // 第一次消费成功
        await c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Authorization_code,
            Code = code,
            ClientId = TestClientIdGuid,
            TenantId = TestTenantId,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
        });

        // 第二次重放应 throw (rotate-once)
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Authorization_code,
            Code = code,
            ClientId = TestClientIdGuid,
            TenantId = TestTenantId,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
        }));
    }

    [Fact]
    [Trait("Fn", "M04.F03.I08")]
    public async Task Token_redirectUriMismatch_throws()
    {
        var (db, _, c) = Build();
        var code = (await c.Authorize(new AuthorizeCodeRequest
        {
            ClientId = TestClientIdGuid,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "lab.read",
            State = "x",
            TenantId = TestTenantId,
        })).Code;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Authorization_code,
            Code = code,
            ClientId = TestClientIdGuid,
            TenantId = TestTenantId,
            RedirectUri = "https://attacker.example.com/callback",
        }));
    }

    [Fact]
    [Trait("Fn", "M04.F03.I09")]
    public async Task Token_refreshToken_happyPath()
    {
        var (db, _, c) = Build();
        // 先 authorize + 拿 access+refresh
        var code = (await c.Authorize(new AuthorizeCodeRequest
        {
            ClientId = TestClientIdGuid,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "lab.read",
            State = "x",
            TenantId = TestTenantId,
        })).Code;
        var first = await c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Authorization_code,
            Code = code,
            ClientId = TestClientIdGuid,
            TenantId = TestTenantId,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
        });

        // 用 refresh 旋转换发
        var second = await c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Refresh_token,
            RefreshToken = first.RefreshToken,
            ClientId = TestClientIdGuid,
            TenantId = TestTenantId,
        });
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);  // 新 refresh
        Assert.NotEqual(first.AccessToken, second.AccessToken);    // 新 access
    }

    [Fact]
    [Trait("Fn", "M04.F03.I09")]
    public async Task Token_refreshTokenReuse_throws()
    {
        var (db, _, c) = Build();
        var code = (await c.Authorize(new AuthorizeCodeRequest
        {
            ClientId = TestClientIdGuid,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "lab.read",
            State = "x",
            TenantId = TestTenantId,
        })).Code;
        var first = await c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Authorization_code,
            Code = code,
            ClientId = TestClientIdGuid,
            TenantId = TestTenantId,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
        });

        // 第一次 refresh 成功
        await c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Refresh_token,
            RefreshToken = first.RefreshToken,
            ClientId = TestClientIdGuid,
            TenantId = TestTenantId,
        });

        // 重放旧 refresh 应 throw
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Refresh_token,
            RefreshToken = first.RefreshToken,
            ClientId = TestClientIdGuid,
            TenantId = TestTenantId,
        }));
    }
}