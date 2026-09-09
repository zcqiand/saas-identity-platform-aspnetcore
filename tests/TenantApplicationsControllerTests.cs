using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbApp = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantApplication;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// M00.F05 — 租户应用订阅 CRUD（guard-first，EF InMemory）。
/// 2026-09-10 审计 critical 补齐：缺 TenantApplicationsController → DI 500。
/// clientId 按字符串列 oauth_clients.client_id 比对（非 UUID）。
/// </summary>
public class TenantApplicationsControllerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static (AppDbContext db, TenantApplicationsController ctrl) Make(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name).Options;
        var db = new AppDbContext(options);
        db.OauthClients.Add(new OauthClient
        {
            Id = Guid.NewGuid(), ClientId = "lab-management", ClientSecret = "s",
            ClientName = "Lab", GrantTypes = "authorization_code",
            RedirectUris = "https://lab.xiangru.uk/cb", Scopes = "openid",
            AccessTokenValidity = 3600, RefreshTokenValidity = 86400,
            AutoApprove = false, Status = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        var guard = new TenantGuard(new StubTenantContext { TenantId = TenantId.ToString() });
        var ctrl = new TenantApplicationsController(db, guard)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (db, ctrl);
    }

    [Fact]
    [Trait("Fn", "M00.F05.I02")]
    public async Task ApplicationsPost_subscribesClient()
    {
        var (_, ctrl) = Make("tenant-apps-post");
        var dto = await ctrl.ApplicationsPost(TenantId.ToString(), new SubscribeTenantApplicationRequest
        {
            ClientId = "lab-management",
            ExpireTime = DateTimeOffset.UtcNow.AddYears(1),
        });
        Assert.Equal("lab-management", dto.ClientId);
        Assert.Equal(TenantId, dto.TenantId);
    }

    [Fact]
    [Trait("Fn", "M00.F05.I02")]
    public async Task ApplicationsPost_unknownClient_throws404()
    {
        var (_, ctrl) = Make("tenant-apps-post-404");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ctrl.ApplicationsPost(
            TenantId.ToString(),
            new SubscribeTenantApplicationRequest { ClientId = "nope", ExpireTime = DateTimeOffset.UtcNow }));
    }

    [Fact]
    [Trait("Fn", "M00.F05.I02")]
    public async Task ApplicationsPost_duplicateSubscription_throws()
    {
        var (db, ctrl) = Make("tenant-apps-dup");
        await ctrl.ApplicationsPost(TenantId.ToString(), new SubscribeTenantApplicationRequest
        {
            ClientId = "lab-management", ExpireTime = DateTimeOffset.UtcNow.AddYears(1),
        });
        await Assert.ThrowsAsync<ArgumentException>(() => ctrl.ApplicationsPost(
            TenantId.ToString(), new SubscribeTenantApplicationRequest
            {
                ClientId = "lab-management", ExpireTime = DateTimeOffset.UtcNow.AddYears(2),
            }));
        // 拒绝后不落第二行
        Assert.Equal(1, await db.TenantApplications.CountAsync(ta => ta.TenantId == TenantId));
    }

    [Fact]
    [Trait("Fn", "M00.F05.I01")]
    public async Task ApplicationsGet_listsTenantScoped()
    {
        var (db, ctrl) = Make("tenant-apps-list");
        // 另一租户的同名订阅不得混入
        db.TenantApplications.Add(new DbApp
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), ClientId = "lab-management",
            Status = 1, ExpireTime = DateTime.UtcNow.AddYears(1),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await ctrl.ApplicationsPost(TenantId.ToString(), new SubscribeTenantApplicationRequest
        {
            ClientId = "lab-management", ExpireTime = DateTimeOffset.UtcNow.AddYears(1),
        });
        var page = await ctrl.ApplicationsGet(TenantId.ToString(), 0, 20);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    [Trait("Fn", "M00.F05.I03")]
    public async Task ApplicationsPatch_updatesStatusAndExpiry()
    {
        var (_, ctrl) = Make("tenant-apps-patch");
        await ctrl.ApplicationsPost(TenantId.ToString(), new SubscribeTenantApplicationRequest
        {
            ClientId = "lab-management", ExpireTime = DateTimeOffset.UtcNow.AddYears(1),
        });
        var updated = await ctrl.ApplicationsPatch(TenantId.ToString(), "lab-management", new UpdateTenantApplicationRequest
        {
            Status = 2, ExpireTime = DateTimeOffset.UtcNow.AddMonths(6),
        });
        Assert.Equal(2, updated.Status);
    }

    [Fact]
    [Trait("Fn", "M00.F05.I04")]
    public async Task ApplicationsDelete_removesSubscription()
    {
        var (db, ctrl) = Make("tenant-apps-delete");
        await ctrl.ApplicationsPost(TenantId.ToString(), new SubscribeTenantApplicationRequest
        {
            ClientId = "lab-management", ExpireTime = DateTimeOffset.UtcNow.AddYears(1),
        });
        await ctrl.ApplicationsDelete(TenantId.ToString(), "lab-management");
        Assert.Equal(0, await db.TenantApplications.CountAsync(ta => ta.TenantId == TenantId));
    }

    [Fact]
    [Trait("Fn", "M00.F01.I03")]
    public async Task Applications_ops_guardRejectsForeignTenant()
    {
        var (_, ctrl) = Make("tenant-apps-guard");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctrl.ApplicationsGet(
            Guid.NewGuid().ToString(), 0, 20));
    }
}
