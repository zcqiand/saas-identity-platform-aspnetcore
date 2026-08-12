using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

public class TenantGuardTests
{
    [Fact]
    [Trait("Fn", "M00.F01.I03")]
    public void VerifyPathTenant_throwsOnMismatch()
    {
        var ctx = new StubTenantContext { TenantId = "tenant-A" };
        var guard = new TenantGuard(ctx);
        Assert.Throws<UnauthorizedAccessException>(() => guard.VerifyPathTenant("tenant-B"));
    }

    [Fact]
    [Trait("Fn", "M00.F01.I03")]
    public void VerifyPathTenant_acceptsMatch()
    {
        var ctx = new StubTenantContext { TenantId = "tenant-A" };
        var guard = new TenantGuard(ctx);
        guard.VerifyPathTenant("tenant-A"); // should not throw
    }

    [Fact]
    [Trait("Fn", "M01.F01.I01")]
    public void ListUsers_returnsPagedResult()
    {
        var svc = new Service.TenantUsersService();
        var p = svc.ListUsers(Guid.NewGuid().ToString(), 0, 20, null);
        Assert.Single(p.Items);
    }

    [Fact]
    [Trait("Fn", "M01.F01.I02")]
    public void CreateUser_returnsUser()
    {
        var svc = new Service.TenantUsersService();
        var u = svc.CreateUser(Guid.NewGuid().ToString(), "alice", "alice@example.com");
        Assert.Equal("alice", u.Username);
        Assert.Equal("invited", u.Status);
    }

    [Fact]
    [Trait("Fn", "M01.F01.I05")]
    public void DeleteUser_isNoOp()
    {
        var svc = new Service.TenantUsersService();
        svc.DeleteUser(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
    }

    private class StubTenantContext : TenantContext
    {
        public string TenantId { get; set; } = "";
        public StubTenantContext() : base(null) { }
        public override string? CurrentTenantId() => TenantId;
    }
}