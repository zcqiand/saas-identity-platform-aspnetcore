using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// M04.F01 / M04.F02.I01 — 平台 admin OAuth client CRUD（EF InMemory roundtrip）。
/// 审计 2026-09-10 critical：缺 AdminClientsController → DI 500。
/// clientId 比对语义：字符串列 oauth_clients.client_id（与 public ClientsController 一致，非 UUID）。
/// </summary>
public class AdminClientsControllerTests
{
    private static AppDbContext MakeDb(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name).Options;
        return new AppDbContext(options);
    }

    private static CreateOAuthClientRequest MakeCreate() => new()
    {
        ClientId = "lab-management",
        ClientName = "Lab",
        ClientSecret = "secret",
        GrantTypes = "authorization_code",
        RedirectUris = "https://lab.xiangru.uk/cb",
        Scopes = "openid profile",
        AccessTokenValidity = 3600,
        RefreshTokenValidity = 86400,
        AutoApprove = false,
    };

    [Fact]
    [Trait("Fn", "M04.F01.I02")]
    public async Task ClientsPost_persistsAndReturnsDto()
    {
        var db = MakeDb("admin-clients-post");
        var ctrl = new AdminClientsController(db);
        var dto = await ctrl.ClientsPost(MakeCreate());
        Assert.Equal("lab-management", dto.ClientId);
        Assert.Equal(1, await db.OauthClients.CountAsync());
    }

    [Fact]
    [Trait("Fn", "M04.F01.I01")]
    public async Task ClientsGet_listsPaged()
    {
        var db = MakeDb("admin-clients-list");
        var ctrl = new AdminClientsController(db);
        await ctrl.ClientsPost(MakeCreate());
        var page = await ctrl.ClientsGet(0, 20);
        Assert.Equal(1, page.Total);
        Assert.Single(page.Items);
    }

    [Fact]
    [Trait("Fn", "M04.F01.I04")]
    public async Task ClientsGet_byClientId_returnsDetail()
    {
        var db = MakeDb("admin-clients-detail");
        var ctrl = new AdminClientsController(db);
        await ctrl.ClientsPost(MakeCreate());
        var dto = await ctrl.ClientsGet("lab-management");
        Assert.Equal("Lab", dto.ClientName);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ctrl.ClientsGet("nope"));
    }

    [Fact]
    [Trait("Fn", "M04.F01.I04")]
    public async Task ClientsPatch_updatesName()
    {
        var db = MakeDb("admin-clients-patch");
        var ctrl = new AdminClientsController(db);
        await ctrl.ClientsPost(MakeCreate());
        var updated = await ctrl.ClientsPatch("lab-management", new UpdateOAuthClientRequest
        {
            ClientName = "Lab v2",
        });
        Assert.Equal("Lab v2", updated.ClientName);
    }

    [Fact]
    [Trait("Fn", "M04.F01.I05")]
    public async Task ClientsDelete_removesAndSets204()
    {
        var db = MakeDb("admin-clients-del");
        var ctrl = new AdminClientsController(db);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        await ctrl.ClientsPost(MakeCreate());
        await ctrl.ClientsDelete("lab-management");
        Assert.Equal(0, await db.OauthClients.CountAsync());
        Assert.Equal(204, ctrl.Response.StatusCode);
    }

    [Fact]
    [Trait("Fn", "M04.F02.I01")]
    public async Task Status_patchUpdatesStatus()
    {
        var db = MakeDb("admin-clients-status");
        var ctrl = new AdminClientsController(db);
        await ctrl.ClientsPost(MakeCreate());
        var updated = await ctrl.Status("lab-management", new Body { Status = 2 });
        Assert.Equal(2, updated.Status);
    }
}
