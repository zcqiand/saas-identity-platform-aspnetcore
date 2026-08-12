using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M02.F01 角色 CRUD + M02.F02 权限矩阵（tenant-scoped）。
/// </summary>
public class TenantRolesControllerTests : TestBase
{
    private TenantRolesController NewC()
    {
        var ctx = new StubTenantContext { TenantId = InMemoryStore.AcmeId.ToString() };
        return new TenantRolesController(new TenantGuard(ctx));
    }

    [Fact]
    [Trait("Fn", "M02.F01.I01")]
    public async Task List_returnsRolesInTenant()
    {
        var c = NewC();
        var res = await c.RolesGet(InMemoryStore.AcmeId.ToString(), null, null);
        Assert.NotEmpty(res.Items);
    }

    [Fact]
    [Trait("Fn", "M02.F01.I02")]
    public async Task Create_addsRole()
    {
        var c = NewC();
        var r = await c.RolesPost(InMemoryStore.AcmeId.ToString(), new() { Code = "viewer", Name = "查看者" });
        Assert.NotEqual(Guid.Empty, r.Id);
        Assert.Equal("viewer", r.Code);
    }

    [Fact]
    [Trait("Fn", "M02.F01.I03")]
    public async Task GetById_returnsRole()
    {
        var c = NewC();
        var r = await c.RolesGet(InMemoryStore.AcmeId.ToString(), InMemoryStore.AdminRoleId.ToString());
        Assert.Equal("admin", r.Code);
    }

    [Fact]
    [Trait("Fn", "M02.F01.I04")]
    public async Task Patch_updatesName()
    {
        var c = NewC();
        var r = await c.RolesPatch(InMemoryStore.AcmeId.ToString(), InMemoryStore.AdminRoleId.ToString(), new() { Name = "管理员v2" });
        Assert.Equal("管理员v2", r.Name);
    }

    [Fact]
    [Trait("Fn", "M02.F01.I05")]
    public async Task Delete_removesRole()
    {
        var c = NewC();
        var before = (await c.RolesGet(InMemoryStore.AcmeId.ToString(), null, null)).Items.Count;
        await c.RolesDelete(InMemoryStore.AcmeId.ToString(), InMemoryStore.MemberRoleId.ToString());
        var after = (await c.RolesGet(InMemoryStore.AcmeId.ToString(), null, null)).Items.Count;
        Assert.Equal(before - 1, after);
    }

    [Fact]
    [Trait("Fn", "M02.F02.I01")]
    public async Task Permissions_setsRolePermissions()
    {
        var c = NewC();
        var r = await c.Permissions(InMemoryStore.AcmeId.ToString(), InMemoryStore.MemberRoleId.ToString(), new()
        {
            PermissionIds = new List<string> { "users.read", "users.write", "roles.read" },
        });
        Assert.Equal(3, r.PermissionIds.Count);
    }
}