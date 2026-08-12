using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M01.F01 用户 CRUD（tenant-scoped）+ M01.F02 角色分配/状态切换/邀请。
/// 替换手写 Service.TenantUsersService（v0.2.0）。
/// 每个 tenant-scoped 方法第一行调 _tenantGuard.VerifyPathTenant(tenantId)。
/// </summary>
public class TenantUsersController : TenantUsersControllerBase
{
    private readonly TenantGuard _guard;
    public TenantUsersController(TenantGuard guard) { _guard = guard; }

    public override Task<Response11> UsersGet(string tenantId, int? page, int? pageSize, UserStatus? status)
    {
        // M01.F01.I01 用户列表（tenant-scoped）
        _guard.VerifyPathTenant(tenantId);
        var gid = Guid.Parse(tenantId);
        var items = InMemoryStore.Users.Where(u => u.TenantId == gid).ToList();
        return Task.FromResult(new Response11
        {
            Items = items,
            Page = page ?? 1,
            PageSize = pageSize ?? items.Count,
            Total = items.Count,
        });
    }

    public override Task<User> UsersPost(string tenantId, CreateUserRequest body)
    {
        // M01.F01.I02 创建用户（CreateUserRequest 不含 status，初始为 Invited）
        _guard.VerifyPathTenant(tenantId);
        var u = new User
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse(tenantId),
            Username = body.Username,
            Email = body.Email,
            DisplayName = body.DisplayName,
            Status = UserStatus.Invited,
            RoleIds = body.RoleIds ?? new List<string>(),
        };
        InMemoryStore.Users.Add(u);
        return Task.FromResult(u);
    }

    public override Task<User> Invitations(string tenantId, Body6 body)
    {
        // M01.F02.I02 邀请用户
        _guard.VerifyPathTenant(tenantId);
        var u = new User
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse(tenantId),
            Username = body.Email,
            Email = body.Email,
            Status = UserStatus.Invited,
            RoleIds = body.RoleIds ?? new List<string>(),
        };
        InMemoryStore.Users.Add(u);
        return Task.FromResult(u);
    }

    public override Task<User> UsersGet(string tenantId, string userId)
    {
        // M01.F01.I03 用户详情
        _guard.VerifyPathTenant(tenantId);
        return Task.FromResult(InMemoryStore.Users.First(u => u.Id == Guid.Parse(userId)));
    }

    public override Task<User> UsersPatch(string tenantId, string userId, UpdateUserRequest body)
    {
        // M01.F01.I04 更新用户
        _guard.VerifyPathTenant(tenantId);
        var u = InMemoryStore.Users.First(x => x.Id == Guid.Parse(userId));
        if (body.Email != null) u.Email = body.Email;
        u.Status = body.Status;
        if (body.DisplayName != null) u.DisplayName = body.DisplayName;
        return Task.FromResult(u);
    }

    public override Task UsersDelete(string tenantId, string userId)
    {
        // M01.F01.I05 删除用户
        _guard.VerifyPathTenant(tenantId);
        InMemoryStore.Users.RemoveAll(x => x.Id == Guid.Parse(userId));
        return Task.CompletedTask;
    }

    public override Task<User> Roles(string tenantId, string userId, Body7 body)
    {
        // M01.F01.I06 / M01.F02.I01 分配角色（用户列表入口 + 详细接口）
        _guard.VerifyPathTenant(tenantId);
        var u = InMemoryStore.Users.First(x => x.Id == Guid.Parse(userId));
        u.RoleIds = body.RoleIds.ToList();
        return Task.FromResult(u);
    }

    public override Task<User> Status(string tenantId, string userId, Body8 body)
    {
        // M01.F02.I03 状态切换
        _guard.VerifyPathTenant(tenantId);
        var u = InMemoryStore.Users.First(x => x.Id == Guid.Parse(userId));
        u.Status = body.Status;
        return Task.FromResult(u);
    }
}