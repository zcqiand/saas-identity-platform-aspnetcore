using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbMembership = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantMember;
using DbUser = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysUser;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
// alias 避免与 NSwag-generated DTO `TenantMembership` 冲突
using ApiMembership = Saas.Identity.AspNetCore.Controllers.Generated.TenantMembership;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F02 当前用户身份（whoami + 跨租户切换 + 我的菜单）。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// v0.5.0：9/7 重组 — TenantMembership → TenantMember；membership.roleIds 列 DROP
/// （sys_user / sys_role 经 tenant_member_role M:N 关联），改用 SysRole.Menus
/// skip nav 取授权菜单。MenuStatus / MenuType 从 PG enum 改 smallint。
/// </summary>
public class MeController : MeControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    public MeController(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    private Guid? CurrentUserId()
    {
        var sub = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? _http.HttpContext?.User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static string B64Url(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .Replace("=", "").Replace("+", "-").Replace("/", "_");

    private static string IssueAccessToken(Guid userId, Guid tenantId) =>
        $"{B64Url("{\"alg\":\"none\"}")}.{B64Url($"{{\"sub\":\"{userId}\",\"tenant_id\":\"{tenantId}\",\"exp\":{((DateTimeOffset)DateTime.UtcNow.AddHours(1)).ToUnixTimeSeconds()}}}")}.dev-placeholder";

    // === DTO 转换 ===

    private static ApiMembership ToMembershipDto(DbMembership m, IList<Guid> roleIds) => new()
    {
        Id = m.Id,
        UserId = m.UserId,
        TenantId = m.TenantId,
        RoleIds = roleIds.Select(g => g.ToString()).ToList(),
        Status = ToMembershipStatus(m.Status),
        JoinedAt = m.CreatedAt,
    };

    private static MembershipStatus ToMembershipStatus(short s) => s switch
    {
        2 => MembershipStatus.Invited,
        3 => MembershipStatus.Removed,
        _ => MembershipStatus.Active,
    };

    public override async Task<CurrentUser> Me()
    {
        var uid = CurrentUserId()
            ?? throw new UnauthorizedAccessException("no JWT sub claim");
        var user = await _db.SysUsers.FirstOrDefaultAsync(u => u.Id == uid)
            ?? throw new KeyNotFoundException("user not found");
        // 用 SysRole.Roles skip nav 拿每个 membership 的角色 ID 集合
        var memberships = await _db.TenantMembers
            .Include(m => m.Roles)
            .Where(m => m.UserId == uid)
            .ToListAsync();
        var dtos = memberships
            .Where(m => m.Status != 3) // removed
            .Select(m => ToMembershipDto(m, m.Roles.Select(r => r.Id).ToList()))
            .ToList();
        var currentTenantId = memberships.FirstOrDefault()?.TenantId ?? Guid.Empty;
        return new CurrentUser
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.Email, // SysUser 无 DisplayName；DTO 字段保留兼容占位
            Memberships = dtos,
            CurrentTenantId = currentTenantId,
        };
    }

    public override async Task<IDictionary<string, ICollection<EffectiveMenuNode>>> Menus()
    {
        var uid = CurrentUserId();
        if (uid is null)
        {
            var session = _http.HttpContext?.Items[SaasSessionMiddleware.ItemsKey] as SaasSession;
            if (session is null)
                throw new UnauthorizedAccessException("saas session or Bearer token required for /me/menus");
            uid = session.UserId;
        }

        // 取 user 的所有 active memberships → 关联 roles → union role ids
        var memberships = await _db.TenantMembers
            .Include(m => m.Roles)
            .Where(m => m.UserId == uid && m.Status != 3)
            .ToListAsync();
        var roleIds = memberships.SelectMany(m => m.Roles.Select(r => r.Id)).Distinct().ToList();
        if (roleIds.Count == 0)
        {
            return new Dictionary<string, ICollection<EffectiveMenuNode>>();
        }

        // 9/7 重组：sys_role_menu 用 EF skip nav，不再有 RoleMenuGrants 实体。
        // 一次 include 取所有 role 的 menus（distinct by menu id）。
        var rolesWithMenus = await _db.SysRoles
            .Include(r => r.Menus)
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync();
        var grantedMenuIds = rolesWithMenus
            .SelectMany(r => r.Menus.Select(m => m.Id))
            .Distinct()
            .ToHashSet();
        if (grantedMenuIds.Count == 0)
        {
            return new Dictionary<string, ICollection<EffectiveMenuNode>>();
        }

        // 树装配 + 父链补全
        var allMenuIds = new HashSet<Guid>(grantedMenuIds);
        List<SysMenu> menus;
        while (true)
        {
            menus = await _db.SysMenus
                .Where(m => allMenuIds.Contains(m.Id) && m.Status == 1) // 1 = active
                .ToListAsync();
            var missingParents = menus
                .Where(m => m.ParentId != Guid.Empty && !allMenuIds.Contains(m.ParentId))
                .Select(m => m.ParentId)
                .ToHashSet();
            if (missingParents.Count == 0) break;
            allMenuIds.UnionWith(missingParents);
        }

        var clientIds = menus.Select(m => m.ClientId).Distinct().ToList();
        var apps = await _db.OauthClients.Where(a => clientIds.Contains(a.ClientId)).ToListAsync();
        var codeByClientId = apps.ToDictionary(a => a.ClientId, a => a.ClientName);

        var byId = menus.ToDictionary(m => m.Id);
        var childrenByParent = menus
            .Where(m => m.ParentId != Guid.Empty && byId.ContainsKey(m.ParentId))
            .GroupBy(m => m.ParentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        EffectiveMenuNode BuildNode(SysMenu m)
        {
            var dto = new EffectiveMenuNode
            {
                Id = m.Id,
                AppId = Guid.Parse(m.ClientId),
                ParentId = m.ParentId,
                Code = m.Path ?? m.Title, // old Code → use Path
                Name = m.Title,
                Path = m.Path,
                Icon = m.Icon,
                Type = m.Type switch
                {
                    1 => MenuType.Group,
                    2 => MenuType.Page,
                    _ => MenuType.Action,
                },
                SortOrder = m.SortOrder,
                Children = new List<EffectiveMenuNode>(),
            };
            if (childrenByParent.TryGetValue(m.Id, out var children))
            {
                foreach (var c in children.OrderBy(c => c.SortOrder))
                {
                    dto.Children.Add(BuildNode(c));
                }
            }
            return dto;
        }

        var grouped = menus
            .Where(m => m.ParentId == Guid.Empty && codeByClientId.ContainsKey(m.ClientId))
            .Select(m => BuildNode(m))
            .GroupBy(n => codeByClientId[n.AppId.ToString()])
            .ToDictionary(g => g.Key, g => (ICollection<EffectiveMenuNode>)g.ToList());
        return grouped;
    }

    public override async Task<ICollection<ApiMembership>> Tenants()
    {
        var uid = CurrentUserId()
            ?? throw new UnauthorizedAccessException("no JWT sub claim");
        var memberships = await _db.TenantMembers
            .Include(m => m.Roles)
            .Where(m => m.UserId == uid && m.Status != 3)
            .ToListAsync();
        return memberships.Select(m => ToMembershipDto(m, m.Roles.Select(r => r.Id).ToList())).ToList();
    }

    public override async Task<SwitchTenantResponse> Switch(string tenantId)
    {
        var uid = CurrentUserId()
            ?? throw new UnauthorizedAccessException("Bearer sub required for tenant switch");
        var tid = Guid.Parse(tenantId);
        var member = await _db.TenantMembers
            .FirstOrDefaultAsync(m => m.UserId == uid && m.TenantId == tid && m.Status != 3);
        if (member is null)
            throw new KeyNotFoundException($"user {uid} is not a member of tenant {tid}");
        return new SwitchTenantResponse
        {
            AccessToken = IssueAccessToken(uid, tid),
            RefreshToken = $"refresh-{uid}-{((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds()}",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TenantId = tid,
        };
    }
}