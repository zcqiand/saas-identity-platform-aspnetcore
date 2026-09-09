using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbMembership = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantMember;
using DbSysMenu = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysMenu;
using DbUser = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysUser;
using DtoSysMenu = Saas.Identity.AspNetCore.Controllers.Generated.SysMenu;
using DtoTenantMember = Saas.Identity.AspNetCore.Controllers.Generated.TenantMember;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
// disambiguate DTO vs entity
using DtoSysUser = Saas.Identity.AspNetCore.Controllers.Generated.SysUser;
using DtoTenantMember2 = Saas.Identity.AspNetCore.Controllers.Generated.TenantMember;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F02 当前用户身份（whoami + 跨租户切换 + 我的菜单）。
/// v0.5.0：9/7 重组 + NSwag 重 emit — MeControllerBase 的 Menus/Tenants/Switch 都
/// 加了 clientId 必填 query 参数；Tenants 返回 TenantMember 实体（DTO 字段相同）。
/// status 走 int 1/2/3。
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
            .Where(m => m.UserId == uid)
            .ToListAsync();
        var dtos = memberships
            .Where(m => m.Status != 3)
            .Select(m => new DtoTenantMember
            {
                Id = m.Id,
                UserId = m.UserId,
                TenantId = m.TenantId,
                MemberName = m.MemberName,
                IsOwner = m.IsOwner,
                Status = (TenantMemberStatus)(int)m.Status,
                CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(m.CreatedAt, DateTimeKind.Utc)),
                UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(m.UpdatedAt, DateTimeKind.Utc)),
            })
            .ToList();
        var currentTenantId = memberships.FirstOrDefault()?.TenantId ?? Guid.Empty;
        // v0.5.0 NSwag 重 emit：CurrentUser 形状变 { user: SysUser, memberships: List<TenantMember>, currentTenantId, clientId }
        // — Id/Email/DisplayName 顶层字段取消，包到 user 子对象里。
        return new CurrentUser
        {
            User = new DtoSysUser
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Mobile = user.Mobile,
                Status = (SysUserStatus)(int)user.Status,
                FailedAttempts = user.FailedAttempts,
                LockedUntil = user.LockedUntil.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(user.LockedUntil.Value, DateTimeKind.Utc)) : default,
                CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc)),
                UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(user.UpdatedAt, DateTimeKind.Utc)),
            },
            Memberships = dtos,
            CurrentTenantId = currentTenantId,
            ClientId = "",
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
            .Where(m => m.UserId == uid && m.Status != 3)
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
            menus = await _db.SysMenus
                .Where(m => allMenuIds.Contains(m.Id) && m.ClientId == clientId && m.Status == 1)
                .ToListAsync();
            var missingParents = menus
                .Where(m => !allMenuIds.Contains(m.ParentId) && m.ParentId != Guid.Empty)
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
                Type = m.Type switch
                {
                    1 => SysMenuType.Menu,
                    2 => SysMenuType.Menu,
                    _ => SysMenuType.Directory,
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
            .GroupBy(n => codeByClientId[n.ClientId])
            .ToDictionary(g => g.Key, g => (ICollection<EffectiveMenuNode>)g.ToList());
        return grouped;
    }

    public override async Task<ICollection<DtoTenantMember>> Tenants(string clientId)
    {
        var uid = CurrentUserId()
            ?? throw new UnauthorizedAccessException("no JWT sub claim");
        var memberships = await _db.TenantMembers
            .Include(m => m.Roles)
            .Where(m => m.UserId == uid && m.Status != 3)
            .ToListAsync();
        return memberships.Select(m => new DtoTenantMember
        {
            Id = m.Id,
            UserId = m.UserId,
            TenantId = m.TenantId,
            MemberName = m.MemberName,
            IsOwner = m.IsOwner,
            Status = (TenantMemberStatus)(int)m.Status,
            CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(m.CreatedAt, DateTimeKind.Utc)),
            UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(m.UpdatedAt, DateTimeKind.Utc)),
        }).ToList();
    }

    public override async Task<SwitchTenantResponse> Switch(string tenantId, string clientId)
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
            AccessToken = _jwt.IssueAccessToken(uid, tid),
            RefreshToken = JwtIssuer.GenerateRefreshToken(uid),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_jwt.TtlSeconds),
            TenantId = tid,
        };
    }
}