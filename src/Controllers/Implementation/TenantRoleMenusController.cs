using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M09 角色菜单授权（tenant-scoped）。
/// M09.F01.I01 角色已授权菜单（查询）· M09.F02.I02/I03 设置/清空角色菜单。
/// </summary>
public class TenantRoleMenusController : TenantRoleMenusControllerBase
{
    private readonly TenantGuard _guard;
    public TenantRoleMenusController(TenantGuard guard) { _guard = guard; }

    public override Task<RoleMenuGrant> MenusGet(string tenantId, string roleId)
    {
        // M09.F01.I01 角色已授权菜单
        _guard.VerifyPathTenant(tenantId);
        var rid = Guid.Parse(roleId);
        var grant = InMemoryStore.RoleMenuGrants.FirstOrDefault(g => g.RoleId == rid)
            ?? new RoleMenuGrant { RoleId = rid, MenuIds = new List<string>(), UpdatedAt = DateTime.UtcNow };
        return Task.FromResult(grant);
    }

    public override Task<RoleMenuGrant> MenusPut(string tenantId, string roleId, SetRoleMenusRequest body)
    {
        // M09.F02.I02 设置角色菜单
        _guard.VerifyPathTenant(tenantId);
        var rid = Guid.Parse(roleId);
        var existing = InMemoryStore.RoleMenuGrants.FirstOrDefault(g => g.RoleId == rid);
        if (existing != null)
        {
            existing.MenuIds = body.MenuIds.ToList();
            existing.UpdatedAt = DateTime.UtcNow;
            return Task.FromResult(existing);
        }
        var fresh = new RoleMenuGrant { RoleId = rid, MenuIds = body.MenuIds.ToList(), UpdatedAt = DateTime.UtcNow };
        InMemoryStore.RoleMenuGrants.Add(fresh);
        return Task.FromResult(fresh);
    }

    public override Task MenusDelete(string tenantId, string roleId)
    {
        // M09.F02.I03 清空角色菜单
        _guard.VerifyPathTenant(tenantId);
        InMemoryStore.RoleMenuGrants.RemoveAll(g => g.RoleId == Guid.Parse(roleId));
        return Task.CompletedTask;
    }
}