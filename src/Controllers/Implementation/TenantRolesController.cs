using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M02.F01 角色 CRUD（tenant-scoped）+ M02.F02 权限矩阵。
/// </summary>
public class TenantRolesController : TenantRolesControllerBase
{
    private readonly TenantGuard _guard;
    public TenantRolesController(TenantGuard guard) { _guard = guard; }

    public override Task<Response10> RolesGet(string tenantId, int? page, int? pageSize)
    {
        // M02.F01.I01 角色列表
        _guard.VerifyPathTenant(tenantId);
        var gid = Guid.Parse(tenantId);
        var items = InMemoryStore.Roles.Where(r => r.TenantId == gid).ToList();
        return Task.FromResult(new Response10
        {
            Items = items,
            Page = page ?? 1,
            PageSize = pageSize ?? items.Count,
            Total = items.Count,
        });
    }

    public override Task<Role> RolesPost(string tenantId, CreateRoleRequest body)
    {
        // M02.F01.I02 创建角色
        _guard.VerifyPathTenant(tenantId);
        var r = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse(tenantId),
            Code = body.Code,
            Name = body.Name,
            PermissionIds = new List<string>(),
        };
        InMemoryStore.Roles.Add(r);
        return Task.FromResult(r);
    }

    public override Task<Role> RolesGet(string tenantId, string roleId)
    {
        // M02.F01.I03 角色详情
        _guard.VerifyPathTenant(tenantId);
        return Task.FromResult(InMemoryStore.Roles.First(r => r.Id == Guid.Parse(roleId)));
    }

    public override Task<Role> RolesPatch(string tenantId, string roleId, UpdateRoleRequest body)
    {
        // M02.F01.I04 更新角色
        _guard.VerifyPathTenant(tenantId);
        var r = InMemoryStore.Roles.First(x => x.Id == Guid.Parse(roleId));
        if (body.Name != null) r.Name = body.Name;
        return Task.FromResult(r);
    }

    public override Task RolesDelete(string tenantId, string roleId)
    {
        // M02.F01.I05 删除角色
        _guard.VerifyPathTenant(tenantId);
        InMemoryStore.Roles.RemoveAll(x => x.Id == Guid.Parse(roleId));
        return Task.CompletedTask;
    }

    public override Task<Role> Permissions(string tenantId, string roleId, Body5 body)
    {
        // M02.F02.I01 权限矩阵（设置 role 的 permissionIds）
        _guard.VerifyPathTenant(tenantId);
        var r = InMemoryStore.Roles.First(x => x.Id == Guid.Parse(roleId));
        r.PermissionIds = body.PermissionIds.ToList();
        return Task.FromResult(r);
    }
}