using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M01.F01 + M01.F02 用户 CRUD + 角色分配/状态切换（tenant-scoped）。
/// 每个测试第一行调 TenantGuard.VerifyPathTenant(tenantId)。
/// </summary>
public class TenantUsersControllerTests : TestBase
{
    private TenantUsersController NewC()
    {
        var ctx = new StubTenantContext { TenantId = InMemoryStore.AcmeId.ToString() };
        return new TenantUsersController(new TenantGuard(ctx));
    }

    [Fact]
    [Trait("Fn", "M01.F01.I01")]
    public async Task List_returnsUsersInTenant()
    {
        var c = NewC();
        var res = await c.UsersGet(InMemoryStore.AcmeId.ToString(), null, null, null);
        Assert.NotEmpty(res.Items);
        Assert.All(res.Items, u => Assert.Equal(InMemoryStore.AcmeId, u.TenantId));
    }

    [Fact]
    [Trait("Fn", "M01.F01.I02")]
    public async Task Create_addsUser()
    {
        var c = NewC();
        var u = await c.UsersPost(InMemoryStore.AcmeId.ToString(), new()
        {
            Username = "charlie",
            Email = "charlie@acme.io",
            DisplayName = "Charlie",
            Password = "secret",
        });
        Assert.NotEqual(Guid.Empty, u.Id);
        Assert.Equal(UserStatus.Invited, u.Status);
    }

    [Fact]
    [Trait("Fn", "M01.F01.I03")]
    public async Task GetById_returnsUser()
    {
        var c = NewC();
        var u = await c.UsersGet(InMemoryStore.AcmeId.ToString(), InMemoryStore.AliceId.ToString());
        Assert.Equal("alice", u.Username);
    }

    [Fact]
    [Trait("Fn", "M01.F01.I04")]
    public async Task Patch_updatesEmail()
    {
        var c = NewC();
        var u = await c.UsersPatch(InMemoryStore.AcmeId.ToString(), InMemoryStore.AliceId.ToString(), new() { Email = "alice2@acme.io" });
        Assert.Equal("alice2@acme.io", u.Email);
    }

    [Fact]
    [Trait("Fn", "M01.F01.I05")]
    public async Task Delete_removesUser()
    {
        var c = NewC();
        var before = (await c.UsersGet(InMemoryStore.AcmeId.ToString(), null, null, null)).Items.Count;
        await c.UsersDelete(InMemoryStore.AcmeId.ToString(), InMemoryStore.BobId.ToString());
        var after = (await c.UsersGet(InMemoryStore.AcmeId.ToString(), null, null, null)).Items.Count;
        Assert.Equal(before - 1, after);
    }

    [Fact]
    [Trait("Fn", "M01.F02.I02")]
    public async Task Invite_createsInvitedUser()
    {
        var c = NewC();
        var u = await c.Invitations(InMemoryStore.AcmeId.ToString(), new()
        {
            Email = "dave@acme.io",
            RoleIds = new List<string> { InMemoryStore.MemberRoleId.ToString() },
        });
        Assert.Equal(UserStatus.Invited, u.Status);
        Assert.Single(u.RoleIds);
    }

    [Fact]
    [Trait("Fn", "M01.F02.I03")]
    public async Task Status_changesUserStatus()
    {
        var c = NewC();
        var u = await c.Status(InMemoryStore.AcmeId.ToString(), InMemoryStore.AliceId.ToString(), new() { Status = UserStatus.Suspended });
        Assert.Equal(UserStatus.Suspended, u.Status);
    }
}