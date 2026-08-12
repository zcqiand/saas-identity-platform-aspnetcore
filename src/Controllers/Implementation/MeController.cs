using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F02 当前用户身份（whoami + 跨租户切换 + 我的菜单）。
/// 从 JWT claim 读 currentUserId；当前 scaffold 用 InMemoryStore.AliceId 模拟。
/// </summary>
public class MeController : MeControllerBase
{
    public override Task<CurrentUser> Me()
    {
        // M00.F02.I01 当前用户 whoami
        var u = InMemoryStore.Users.First(x => x.Id == InMemoryStore.AliceId);
        return Task.FromResult(new CurrentUser
        {
            Id = u.Id,
            Email = u.Email,
            Memberships = InMemoryStore.Users
                .Where(x => x.Id == u.Id)
                .Select(x => new TenantMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = x.Id,
                    TenantId = x.TenantId,
                    RoleIds = x.RoleIds,
                    Status = MembershipStatus.Active,
                })
                .ToList(),
            CurrentTenantId = u.TenantId,
        });
    }

    public override Task<IDictionary<string, ICollection<EffectiveMenuNode>>> Menus()
    {
        // M09.F03.I04 我的有效菜单（基于角色-菜单授权）
        var userRoles = InMemoryStore.Users.First(x => x.Id == InMemoryStore.AliceId).RoleIds;
        var myMenuIds = InMemoryStore.RoleMenuGrants
            .Where(g => userRoles.Contains(g.RoleId.ToString()))
            .SelectMany(g => g.MenuIds)
            .Distinct()
            .ToList();
        var myMenus = InMemoryStore.Menus
            .Where(m => myMenuIds.Contains(m.Id.ToString()))
            .Select(m => new EffectiveMenuNode
            {
                Id = m.Id,
                Code = m.Code,
                Name = m.Name,
                AppId = m.AppId,
            })
            .ToList();
        var grouped = myMenus
            .GroupBy(m => m.AppId)
            .ToDictionary(g => g.Key.ToString(), g => (ICollection<EffectiveMenuNode>)g.ToList());
        return Task.FromResult<IDictionary<string, ICollection<EffectiveMenuNode>>>(grouped);
    }

    public override Task<ICollection<TenantMembership>> Tenants()
    {
        // M00.F02.I02 列出我的租户成员关系
        var memberships = InMemoryStore.Users
            .Where(u => u.Id == InMemoryStore.AliceId)
            .Select(u => new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = u.TenantId,
                UserId = u.Id,
                RoleIds = u.RoleIds,
                Status = MembershipStatus.Active,
            })
            .ToList();
        return Task.FromResult<ICollection<TenantMembership>>(memberships);
    }

    public override Task<SwitchTenantResponse> Switch(string tenantId)
    {
        // M00.F02.I03 切换当前租户（返回新 token）
        return Task.FromResult(new SwitchTenantResponse
        {
            AccessToken = $"mock-jwt-after-switch-{tenantId}",
            RefreshToken = "mock-refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            TenantId = Guid.Parse(tenantId),
        });
    }
}