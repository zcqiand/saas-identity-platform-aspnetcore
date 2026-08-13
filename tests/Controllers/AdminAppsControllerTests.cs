using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M04 应用 CRUD（平台 admin）+ M08.F01 应用 CRUD。admin-level。
/// </summary>
public class AdminAppsControllerTests : TestBase
{
    private readonly AdminAppsController _c = new(null!);

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M04.F01.I01")]
    public async Task List_returnsApps()
    {
        var res = await _c.AppsGet(null, null);
        Assert.NotEmpty(res.Items);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M04.F01.I02")]
    public async Task Create_addsApp()
    {
        var a = await _c.AppsPost(new() { Code = "new-app", Name = "新应用", Status = AppStatus.Active });
        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.Equal("new-app", a.Code);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M04.F01.I03")]
    public async Task GetById_returnsApp()
    {
        var a = await _c.AppsGet(InMemoryStore.LabAppId.ToString());
        Assert.Equal("lab-portal", a.Code);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M04.F01.I04")]
    public async Task Patch_updatesName()
    {
        var a = await _c.AppsPatch(InMemoryStore.LabAppId.ToString(), new() { Name = "实验室v2" });
        Assert.Equal("实验室v2", a.Name);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M04.F01.I05")]
    public async Task Delete_removesApp()
    {
        var before = (await _c.AppsGet(null, null)).Items.Count;
        await _c.AppsDelete(InMemoryStore.LabAppId.ToString());
        var after = (await _c.AppsGet(null, null)).Items.Count;
        Assert.Equal(before - 1, after);
    }

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M04.F02.I06")]
    public async Task Status_changesAppStatus()
    {
        var a = await _c.Status(InMemoryStore.LabAppId.ToString(), new() { Status = AppStatus.Disabled });
        Assert.Equal(AppStatus.Disabled, a.Status);
    }
}