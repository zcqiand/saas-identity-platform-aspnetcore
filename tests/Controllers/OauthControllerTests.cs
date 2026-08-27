using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    private static (AppDbContext db, JwtIssuer jwt, OauthController controller, DefaultHttpContext ctx) Build(
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
                ["JWT_SIGNING_KEY"] = TestSigningKey,
                ["JWT_ISSUER"] = "saas-identity-platform",
                ["JWT_AUDIENCE"] = "saas-identity-platform-clients",
                ["JWT_TTL_SECONDS"] = "3600",
            })
            .Build();
        var jwt = new JwtIssuer(config);
        var ctx = new DefaultHttpContext();
        // 默认注入 saas session（与 M04.F03.I01 相符）— 测试 session 缺失时显式跳过
        ctx.Items[SaasSessionMiddleware.ItemsKey] = new SaasSession(
            uid, tid, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        var controller = new OauthController(db, jwt)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };
        return (db, jwt, controller, ctx);
    }

    [Fact]
    [Trait("Fn", "M04.F03.I07")]
    public async Task Authorize_happyPath_returnsCode()
    {
        var (_, _, c, _) = Build();
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
        var (_, _, c, _) = Build();
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
        var (_, _, c, _) = Build();
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
        var (_, _, c, _) = Build();
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
        var (db, _, c, _) = Build();
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
        var (db, _, c, _) = Build();
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
        var (db, _, c, _) = Build();
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
        var (db, _, c, _) = Build();
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
        var (db, _, c, _) = Build();
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

    // === M04.F03.I01 Authorize 检查 saas session ===

    [Fact]
    [Trait("Fn", "M04.F03.I01")]
    public async Task Authorize_noSession_throwsUnauthorized()
    {
        // 默认 Build() 注入 saas session -> 显式清掉验证未登录场景
        var (_, _, c, ctx) = Build();
        ctx.Items.Remove(SaasSessionMiddleware.ItemsKey);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Authorize(new AuthorizeCodeRequest
        {
            ClientId = TestClientIdGuid,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "lab.read",
            State = "x",
            TenantId = TestTenantId,
        }));
    }

    [Fact]
    [Trait("Fn", "M04.F03.I01")]
    public async Task Authorize_withValidSession_returnsCode()
    {
        // 注入 saas session 后 Authorize 返 code
        var (_, _, c, ctx) = Build();
        var session = new SaasSession(TestTenantId, TestTenantId, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        ctx.Items["saasSession"] = session;
        var res = await c.Authorize(new AuthorizeCodeRequest
        {
            ClientId = TestClientIdGuid,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "lab.read",
            State = "session-state",
            TenantId = TestTenantId,
        });
        Assert.StartsWith("saas-code-", res.Code);
        Assert.Equal("session-state", res.State);
    }

    // === T-11 (PLAN-2026-001) 集成覆盖：login -> session cookie -> authorize -> token ===

    /// <summary>
    /// 集成流程测试共享工具：真实 AuthController.Login 写 cookie ->
    /// 解析 sid -> SaasSessionMiddleware 按真 cookie 头注入 -> 下游控制器消费。
    /// 与单测的区别：session 不是手工塞进 Items，而是走 Login + Middleware 全链路。
    /// </summary>
    internal static class OauthSessionFlow
    {
        public const string FlowUsername = "flow-alice";
        public const string FlowPassword = "dev123456";
        public static readonly Guid FlowAppId = TestAppId;

        /// <summary>集成流程 TestAppDbContext：User + App + OauthCode + Menu（供 me/menus）。</summary>
        internal class FlowTestDbContext : AppDbContext
        {
            public FlowTestDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                base.OnModelCreating(modelBuilder);
                // InMemory 不支持 PG 专属特性 (jsonb / native enum)
                // 与 TestAppDbContext 的差别：不 Ignore Menu - 集成流程要跑 me/menus
                modelBuilder.Ignore<ApiKeyEntity>();
                modelBuilder.Ignore<AuditEventEntity>();
                modelBuilder.Ignore<AuditRetentionPolicyEntity>();
                modelBuilder.Ignore<PermissionEntity>();
                modelBuilder.Ignore<RoleEntity>();
                modelBuilder.Ignore<RolePermissionEntity>();
                modelBuilder.Ignore<RoleMenuGrantEntity>();
                modelBuilder.Ignore<TenantEntity>();
                modelBuilder.Ignore<TenantMembershipEntity>();
            }
        }

        /// <summary>建库 + seed 流程数据（user + app + 1 menu），返回 (db, userId, tenantId)。</summary>
        public static (AppDbContext db, Guid userId, Guid tenantId) BuildFlowDb()
        {
            var dbOpts = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"flow-test-{Guid.NewGuid()}")
                .Options;
            var db = new FlowTestDbContext(dbOpts);
            var uid = Guid.NewGuid();
            var tid = Guid.NewGuid();
            db.Apps.Add(new AppEntity
            {
                Id = TestAppId,
                Code = "lab-management",
                Name = "lab-mgmt",
                Status = "active",
                ClientId = TestClientIdGuid.ToString(),
                RedirectUris = new List<string> { "https://lab-vue.xiangru.uk/login" },
                Scopes = new List<string> { "lab.read" },
                GrantTypes = new List<string> { "authorization_code", "refresh_token" },
                IsFirstParty = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.Users.Add(new UserEntity
            {
                Id = uid,
                TenantId = tid,
                Username = FlowUsername,
                DisplayName = "Flow Alice",
                Email = "flow@lab.local",
                PasswordHash = $"plain:{FlowPassword}",
                Status = "active",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.Menus.Add(new MenuEntity
            {
                Id = Guid.NewGuid(), AppId = TestAppId, Code = "m-dashboard", Name = "工作台",
                Path = "/", Type = "page", Status = "active",
                SortOrder = 1, CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
            return (db, uid, tid);
        }

        public static JwtIssuer NewJwt()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JWT_SIGNING_KEY"] = TestSigningKey,
                    ["JWT_ISSUER"] = "saas-identity-platform",
                    ["JWT_AUDIENCE"] = "saas-identity-platform-clients",
                    ["JWT_TTL_SECONDS"] = "3600",
                })
                .Build();
            return new JwtIssuer(config);
        }

        /// <summary>真登录一次，返回 (authController, ctx, sessionStore)。</summary>
        public static (AuthController auth, DefaultHttpContext ctx, SaasSessionStore sessions) LoginOnce(
            AppDbContext db, SaasSessionStore? sessions = null, FailedLoginStore? failed = null)
        {
            sessions ??= new SaasSessionStore();
            failed ??= new FailedLoginStore();
            var ctx = new DefaultHttpContext();
            var auth = new AuthController(db, NewJwt(), sessions, failed)
            {
                ControllerContext = new ControllerContext { HttpContext = ctx }
            };
            return (auth, ctx, sessions);
        }

        /// <summary>从 Response 的 Set-Cookie 头解析 saasSession sid。</summary>
        public static string? ExtractSid(DefaultHttpContext ctx)
        {
            if (!ctx.Response.Headers.ContainsKey("Set-Cookie")) return null;
            var cookie = ctx.Response.Headers["Set-Cookie"].ToString();
            var prefix = $"{SaasSessionMiddleware.CookieName}=";
            var start = cookie.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0) return null;
            start += prefix.Length;
            var end = cookie.IndexOf(';', start);
            return end < 0 ? cookie[start..] : cookie[start..end];
        }

        /// <summary>按真 cookie 头过一遍 SaasSessionMiddleware，返回注入后的 ctx。</summary>
        public static async Task<DefaultHttpContext> InvokeMiddleware(SaasSessionStore sessions, string? sid)
        {
            var ctx = new DefaultHttpContext();
            if (sid is not null)
                ctx.Request.Headers["Cookie"] = $"{SaasSessionMiddleware.CookieName}={sid}";
            var middleware = new SaasSessionMiddleware(sessions, _ => Task.CompletedTask);
            await middleware.InvokeAsync(ctx);
            return ctx;
        }

        public static OauthController NewOauthController(AppDbContext db, DefaultHttpContext ctx) =>
            new(db, NewJwt())
            {
                ControllerContext = new ControllerContext { HttpContext = ctx }
            };

        /// <summary>authorize 请求体（tenantId 用登录 session 所属租户 - code 与 session 绑定同一租户）。</summary>
        public static AuthorizeCodeRequest NewAuthorizeRequest(Guid tenantId) => new()
        {
            ClientId = TestClientIdGuid,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
            ResponseType = AuthorizeCodeRequestResponseType.Code,
            Scope = "lab.read",
            State = "flow-state",
            TenantId = tenantId,
        };
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public async Task Flow_login_setsSaasSessionCookie_andStoresSession()
    {
        var (db, uid, tid) = OauthSessionFlow.BuildFlowDb();
        var (auth, ctx, sessions) = OauthSessionFlow.LoginOnce(db);

        var res = await auth.Login(new LoginRequest
        {
            Username = OauthSessionFlow.FlowUsername,
            Password = OauthSessionFlow.FlowPassword,
        });

        // Set-Cookie saasSession=<sid>; HttpOnly; SameSite=Lax（序列化大小写不定，忽略大小写比对）
        Assert.True(ctx.Response.Headers.ContainsKey("Set-Cookie"));
        var cookie = ctx.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("saasSession=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        // sid 在 store 里有 session, 且 user/tenant 来自登录用户
        var sid = OauthSessionFlow.ExtractSid(ctx);
        Assert.NotNull(sid);
        var session = sessions.Get(sid);
        Assert.NotNull(session);
        Assert.Equal(uid, session!.UserId);
        Assert.Equal(tid, session.TenantId);
        Assert.Equal(uid, res.UserId);
    }

    [Fact]
    [Trait("Fn", "M03.F01.I02")]
    public async Task Flow_login_5WrongPasswords_thenLocked()
    {
        var (db, _, _) = OauthSessionFlow.BuildFlowDb();
        var (auth, _, _) = OauthSessionFlow.LoginOnce(db,
            failed: new FailedLoginStore(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15)));

        for (int i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.Login(new LoginRequest
            {
                Username = OauthSessionFlow.FlowUsername,
                Password = "wrong-password",
            }));
        }
        // 第 6 次：正确密码也被 423 锁定
        await Assert.ThrowsAsync<AccountLockedException>(() => auth.Login(new LoginRequest
        {
            Username = OauthSessionFlow.FlowUsername,
            Password = OauthSessionFlow.FlowPassword,
        }));
    }

    [Fact]
    [Trait("Fn", "M04.F03.I01")]
    [Trait("Fn", "M04.F03.I02")]
    public async Task Flow_loginCookie_throughMiddleware_authorizeThenToken()
    {
        var (db, _, tid) = OauthSessionFlow.BuildFlowDb();
        var (auth, loginCtx, sessions) = OauthSessionFlow.LoginOnce(db);
        await auth.Login(new LoginRequest
        {
            Username = OauthSessionFlow.FlowUsername,
            Password = OauthSessionFlow.FlowPassword,
        });
        var sid = OauthSessionFlow.ExtractSid(loginCtx);
        Assert.NotNull(sid);

        // 带真 cookie 头过 middleware -> session 注入 Items
        var ctx = await OauthSessionFlow.InvokeMiddleware(sessions, sid);
        var injected = ctx.Items[SaasSessionMiddleware.ItemsKey] as SaasSession;
        Assert.NotNull(injected);

        // authorize -> token 全链路
        var c = OauthSessionFlow.NewOauthController(db, ctx);
        var authorize = await c.Authorize(OauthSessionFlow.NewAuthorizeRequest(tid));
        Assert.StartsWith("saas-code-", authorize.Code);
        var token = await c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Authorization_code,
            Code = authorize.Code,
            ClientId = TestClientIdGuid,
            TenantId = TestTenantId,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
        });
        Assert.StartsWith("ey", token.AccessToken);
        Assert.StartsWith("saas-rt-", token.RefreshToken);
    }

    [Fact]
    [Trait("Fn", "M04.F03.I02")]
    public async Task Flow_token_tenantIdFromSession_bodyTenantIgnored()
    {
        var (db, _, tid) = OauthSessionFlow.BuildFlowDb();
        var (auth, loginCtx, sessions) = OauthSessionFlow.LoginOnce(db);
        await auth.Login(new LoginRequest
        {
            Username = OauthSessionFlow.FlowUsername,
            Password = OauthSessionFlow.FlowPassword,
        });
        var ctx = await OauthSessionFlow.InvokeMiddleware(sessions, OauthSessionFlow.ExtractSid(loginCtx));
        var c = OauthSessionFlow.NewOauthController(db, ctx);

        var authorize = await c.Authorize(OauthSessionFlow.NewAuthorizeRequest(tid));
        // body.TenantId 是伪造的 - Token 仍按 session 注入（不再信请求体）
        var token = await c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Authorization_code,
            Code = authorize.Code,
            ClientId = TestClientIdGuid,
            TenantId = Guid.NewGuid(),
            RedirectUri = "https://lab-vue.xiangru.uk/login",
        });
        Assert.StartsWith("ey", token.AccessToken);
    }

    [Fact]
    [Trait("Fn", "M04.F03.I03")]
    public async Task Flow_refreshRotation_afterFullLogin()
    {
        var (db, _, tid) = OauthSessionFlow.BuildFlowDb();
        var (auth, loginCtx, sessions) = OauthSessionFlow.LoginOnce(db);
        await auth.Login(new LoginRequest
        {
            Username = OauthSessionFlow.FlowUsername,
            Password = OauthSessionFlow.FlowPassword,
        });
        var ctx = await OauthSessionFlow.InvokeMiddleware(sessions, OauthSessionFlow.ExtractSid(loginCtx));
        var c = OauthSessionFlow.NewOauthController(db, ctx);

        var authorize = await c.Authorize(OauthSessionFlow.NewAuthorizeRequest(tid));
        var first = await c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Authorization_code,
            Code = authorize.Code,
            ClientId = TestClientIdGuid,
            TenantId = tid,
            RedirectUri = "https://lab-vue.xiangru.uk/login",
        });
        var second = await c.Token(new TokenRequest
        {
            GrantType = TokenRequestGrantType.Refresh_token,
            RefreshToken = first.RefreshToken,
            ClientId = TestClientIdGuid,
            TenantId = tid,
        });
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    [Trait("Fn", "M04.F03.I01")]
    public async Task Flow_expiredLoginSession_authorizeThrowsUnauthorized()
    {
        var (db, _, _) = OauthSessionFlow.BuildFlowDb();
        // 100ms TTL 的 store - 登录后马上过期
        var (auth, loginCtx, sessions) = OauthSessionFlow.LoginOnce(db,
            sessions: new SaasSessionStore(TimeSpan.FromMilliseconds(100)));
        await auth.Login(new LoginRequest
        {
            Username = OauthSessionFlow.FlowUsername,
            Password = OauthSessionFlow.FlowPassword,
        });
        await Task.Delay(200);

        // 过期 session: middleware 不注入 -> authorize 401
        var ctx = await OauthSessionFlow.InvokeMiddleware(sessions, OauthSessionFlow.ExtractSid(loginCtx));
        Assert.False(ctx.Items.ContainsKey(SaasSessionMiddleware.ItemsKey));
        var c = OauthSessionFlow.NewOauthController(db, ctx);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => c.Authorize(OauthSessionFlow.NewAuthorizeRequest(TestTenantId)));
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    [Trait("Fn", "M04.F03.I01")]
    public async Task Flow_deletedSession_logoutSemantics_authorizeThrowsUnauthorized()
    {
        var (db, _, _) = OauthSessionFlow.BuildFlowDb();
        var (auth, loginCtx, sessions) = OauthSessionFlow.LoginOnce(db);
        await auth.Login(new LoginRequest
        {
            Username = OauthSessionFlow.FlowUsername,
            Password = OauthSessionFlow.FlowPassword,
        });
        var sid = OauthSessionFlow.ExtractSid(loginCtx);
        Assert.NotNull(sid);

        // session 被删（登出语义）: cookie 还在但 store 没了 -> 401
        sessions.Delete(sid!);
        var ctx = await OauthSessionFlow.InvokeMiddleware(sessions, sid);
        Assert.False(ctx.Items.ContainsKey(SaasSessionMiddleware.ItemsKey));
        var c = OauthSessionFlow.NewOauthController(db, ctx);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => c.Authorize(OauthSessionFlow.NewAuthorizeRequest(TestTenantId)));
    }
}