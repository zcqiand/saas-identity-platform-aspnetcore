using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M08 菜单 CRUD（应用下）。admin-level（菜单是 app 级不是 tenant 级）。
/// </summary>
public class AdminAppMenusControllerTests : TestBase
{
    private readonly AdminAppMenusController _c = new(null!);

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M08.F01.I01")]
    public async Task List_returnsMenusInApp()
    {
        var res = await _c.MenusGet(InMemoryStore.LabAppId.ToString());
        Assert.NotEmpty(res);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M08.F01.I02")]
    public async Task Create_addsMenu()
    {
        var m = await _c.MenusPost(InMemoryStore.LabAppId.ToString(), new()
        {
            Code = "new-menu",
            Name = "新菜单",
            Type = MenuType.Page,
            Status = MenuStatus.Active,
        });
        Assert.NotEqual(Guid.Empty, m.Id);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M08.F01.I03")]
    public async Task GetById_returnsMenu()
    {
        var m = await _c.MenusGet(InMemoryStore.LabAppId.ToString(), InMemoryStore.UsersMenuId.ToString());
        Assert.Equal("users", m.Code);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M08.F01.I04")]
    public async Task Patch_updatesName()
    {
        var m = await _c.MenusPatch(InMemoryStore.LabAppId.ToString(), InMemoryStore.UsersMenuId.ToString(), new() { Name = "用户管理v2" });
        Assert.Equal("用户管理v2", m.Name);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M08.F01.I05")]
    public async Task Delete_removesMenu()
    {
        var before = (await _c.MenusGet(InMemoryStore.LabAppId.ToString())).Count;
        await _c.MenusDelete(InMemoryStore.LabAppId.ToString(), InMemoryStore.RolesMenuId.ToString());
        var after = (await _c.MenusGet(InMemoryStore.LabAppId.ToString())).Count;
        Assert.Equal(before - 1, after);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M08.F02.I07")]
    public async Task Parent_setsParentId()
    {
        var m = await _c.Parent(InMemoryStore.LabAppId.ToString(), InMemoryStore.UsersMenuId.ToString(), new() { ParentId = InMemoryStore.RolesMenuId.ToString() });
        Assert.Equal(InMemoryStore.RolesMenuId, m.ParentId);
    }
}