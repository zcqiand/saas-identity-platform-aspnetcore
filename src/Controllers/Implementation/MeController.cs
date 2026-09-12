using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbMembership = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantMember;
using DbSysMenu = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysMenu;
using DbUser = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysUser;
using DtoSysMenu = Saas.Identity.AspNetCore.Controllers.Generated.SysMenu;
using DtoMembership = Saas.Identity.AspNetCore.Controllers.Generated.TenantMembership;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
// disambiguate DTO vs entity
using DtoSysUser = Saas.Identity.AspNetCore.Controllers.Generated.SysUser;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F02 当前用户身份（whoami + 跨租户切换 + 我的菜单）。
/// ADR-0032（2026-09-12）重 emit：CurrentUser 扁平 {id, email?, memberships: TenantMembership[],
/// currentTenantId?}；/me/tenants 返回 TenantMembership[]（msw oracle 同形态）；
/// SwitchTenantResponse 删 clientId。
/// </summary>
public class MeController : MeControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly JwtIssuer _jwt;

    public MeController(AppDbContext db, IHttpContextAccessor http, JwtIssuer jwt)
    {
        _db = db;
        _http = http;
        _jwt = jwt;
    }

    private Guid? CurrentUserId()
    {
        var sub = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? _http.HttpContext?.User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public override async Task<CurrentUser> Me()
    {
        var uid = CurrentUserId()
            ?? throw new UnauthorizedAccessException("no JWT sub claim");
        var user = await _db.SysUsers.FirstOrDefaultAsync(u => u.Id == uid)
            ?? throw new KeyNotFoundException("user not found");
        var memberships = await _db.TenantMembers
            .Include(m => m.Roles)
            .Where(m => m.UserId == uid && m.Status != 0) // disabled 不进 whoami
            .ToListAsync();
        // currentTenantId：JWT tenant_id claim 优先（switch 后上下文），否则第一个 membership
        //（msw oracle handlers-extra /me 同语义）。
        var claimTenant = _http.HttpContext?.User.FindFirstValue("tenant_id");
        var currentTenantId = Guid.TryParse(claimTenant, out var ct) ? ct
            : memberships.FirstOrDefault()?.TenantId ?? Guid.Empty;
        return new CurrentUser
        {
            Id = user.Id,
            Email = user.Email,
            Memberships = MembershipViews.FromEntities(memberships),
            CurrentTenantId = currentTenantId,
        };
    }

    public override async Task<IDictionary<string, ICollection<EffectiveMenuNode>>> Menus(string clientId)
    {
        var uid = CurrentUserId();
        if (uid is null)
        {
            var session = _http.HttpContext?.Items[SaasSessionMiddleware.ItemsKey] as SaasSession;
            if (session is null)
                throw new UnauthorizedAccessException("saas session or Bearer token required for /me/menus");
            uid = session.UserId;
        }

        var memberships = await _db.TenantMembers
            .Include(m => m.Roles)
            .Where(m => m.UserId == uid && m.Status != 0) // disabled（DB 0）不进有效菜单
            .ToListAsync();
        var roleIds = memberships.SelectMany(m => m.Roles.Select(r => r.Id)).Distinct().ToList();
        if (roleIds.Count == 0)
        {
            return new Dictionary<string, ICollection<EffectiveMenuNode>>();
        }

        var rolesWithMenus = await _db.SysRoles
            .Include(r => r.Menus)
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync();
        var grantedMenuIds = rolesWithMenus
            .SelectMany(r => r.Menus.Select(m => m.Id).OfType<Guid>())
            .Distinct()
            .ToHashSet();
        if (grantedMenuIds.Count == 0)
        {
            return new Dictionary<string, ICollection<EffectiveMenuNode>>();
        }

        var allMenuIds = new HashSet<Guid>(grantedMenuIds);
        List<DbSysMenu> menus;
        while (true)
        {
            // 2026-09-12：clientId null/空不过滤（照 TenantRolesController.RolesGet 先例）——
            // 测试调 /me/menus 不带 clientId 时此前 WHERE client_id = NULL → 恒空 {}。
            var q = _db.SysMenus.Where(m => allMenuIds.Contains(m.Id) && m.Status == 1);
            if (!string.IsNullOrEmpty(clientId)) q = q.Where(m => m.ClientId == clientId);
            menus = await q.ToListAsync();
            var missingParents = menus
                .Where(m => !allMenuIds.Contains(m.ParentId) && m.ParentId != Guid.Empty)
                .Select(m => m.ParentId)
                .ToHashSet();
            if (missingParents.Count == 0) break;
            allMenuIds.UnionWith(missingParents);
        }

        var byId = menus.ToDictionary(m => m.Id);
        var childrenByParent = menus
            .Where(m => m.ParentId != Guid.Empty && byId.ContainsKey(m.ParentId))
            .GroupBy(m => m.ParentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        EffectiveMenuNode BuildNode(DbSysMenu m)
        {
            var dto = new EffectiveMenuNode
            {
                Id = m.Id,
                ClientId = m.ClientId,
                ParentId = m.ParentId,
                Title = m.Title,
                Path = m.Path,
                Icon = m.Icon,
                // 2026-09-12 四方 live（I05）码表修正：DB sys_menu.type 1=directory / 2=menu /
                // 3=button（msw oracle menus fixture 同码表）。旧映射把 1/2 都映 Menu、
                // 其余映 Directory，目录节点全退化成 "menu"。
                Type = m.Type switch
                {
                    1 => SysMenuType.Directory,
                    2 => SysMenuType.Menu,
                    3 => SysMenuType.Button,
                    _ => SysMenuType.Menu,
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

        // 2026-09-12：分组键从 client_name 改回 client_id（家族按 app code 分组，
        // 此前 key 是「建筑工程实验室管理系统」这类中文名）。
        var grouped = menus
            .Where(m => m.ParentId == Guid.Empty)
            .Select(m => BuildNode(m))
            .GroupBy(n => n.ClientId)
            .ToDictionary(g => g.Key, g => (ICollection<EffectiveMenuNode>)g.ToList());
        return grouped;
    }

    public override async Task<ICollection<DtoMembership>> Tenants(string clientId)
    {
        var uid = CurrentUserId()
            ?? throw new UnauthorizedAccessException("no JWT sub claim");
        var memberships = await _db.TenantMembers
            .Include(m => m.Roles)
            .Where(m => m.UserId == uid && m.Status != 0) // msw oracle：不过滤 status，仅排除 disabled
            .ToListAsync();
        return MembershipViews.FromEntities(memberships);
    }

    public override async Task<SwitchTenantResponse> Switch(string tenantId, string clientId)
    {
        var uid = CurrentUserId()
            ?? throw new UnauthorizedAccessException("Bearer sub required for tenant switch");
        var tid = Guid.Parse(tenantId);
        var member = await _db.TenantMembers
            .FirstOrDefaultAsync(m => m.UserId == uid && m.TenantId == tid && m.Status != 0); // disabled 不可切换
        if (member is null)
            throw new KeyNotFoundException($"user {uid} is not a member of tenant {tid}");
        return new SwitchTenantResponse
        {
            AccessToken = _jwt.IssueAccessToken(uid, tid),
            RefreshToken = JwtIssuer.GenerateRefreshToken(uid),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_jwt.TtlSeconds),
            TenantId = tid,
        };
    }
}