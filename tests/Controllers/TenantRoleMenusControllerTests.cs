using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M09 角色-菜单授权（tenant-scoped）。
/// </summary>
public class TenantRoleMenusControllerTests : TestBase
{
    private TenantRoleMenusController NewC()
    {
        var ctx = new StubTenantContext { TenantId = InMemoryStore.AcmeId.ToString() };
        return new TenantRoleMenusController(new TenantGuard(ctx), null!);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M09.F01.I01")]
    public async Task Get_returnsGrantForRole()
    {
        var c = NewC();
        var g = await c.MenusGet(InMemoryStore.AcmeId.ToString(), InMemoryStore.AdminRoleId.ToString());
        Assert.NotEmpty(g.MenuIds);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M09.F02.I02")]
    public async Task Put_setsRoleMenus()
    {
        var c = NewC();
        var g = await c.MenusPut(InMemoryStore.AcmeId.ToString(), InMemoryStore.MemberRoleId.ToString(), new()
        {
            MenuIds = new List<string> { InMemoryStore.UsersMenuId.ToString() },
        });
        Assert.Single(g.MenuIds);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M09.F02.I03")]
    public async Task Delete_clearsGrant()
    {
        var c = NewC();
        await c.MenusDelete(InMemoryStore.AcmeId.ToString(), InMemoryStore.AdminRoleId.ToString());
        var g = await c.MenusGet(InMemoryStore.AcmeId.ToString(), InMemoryStore.AdminRoleId.ToString());
        Assert.Empty(g.MenuIds);
    }
}