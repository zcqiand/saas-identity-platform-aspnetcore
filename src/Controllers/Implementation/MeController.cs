using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using DbMembership = Saas.Identity.AspNetCore.Domain.Entities.TenantMembership;
using DbUser = Saas.Identity.AspNetCore.Domain.Entities.User;
// alias 避免与 NSwag-generated DTO `TenantMembership` 冲突
using ApiMembership = Saas.Identity.AspNetCore.Controllers.Generated.TenantMembership;
// alias 避免与 NSwag-generated DTO `Menu` (用于 menu_type enum) 冲突
using DbMenu = Saas.Identity.AspNetCore.Domain.Entities.Menu;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F02 当前用户身份（whoami + 跨租户切换 + 我的菜单）。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext。
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
        $"{B64Url("{\"alg\":\"none\"}")}.{B64Url($"{{\"sub\":\"{userId}\",\"tenant_id\":\"{tenantId}\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}")}.dev-placeholder";

    // === DTO 转换 ===

    private static ApiMembership ToMembershipDto(DbMembership m) => new()
    {
        Id = m.Id,
        UserId = m.UserId,
        TenantId = m.TenantId,
        RoleIds = (m.RoleIds ?? new()).Select(g => g.ToString()).ToList(),
        Status = ToMembershipStatus(m.Status),
        JoinedAt = m.JoinedAt,
    };

    private static MembershipStatus ToMembershipStatus(string s) => s switch
    {
        "active" => MembershipStatus.Active,
        "invited" => MembershipStatus.Invited,
        _ => MembershipStatus.Removed,
    };

    // === endpoints ===

    public override async Task<CurrentUser> Me()
    {
        var uid = CurrentUserId()
            ?? throw new UnauthorizedAccessException("no JWT sub claim");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid)
            ?? throw new KeyNotFoundException("user not found");
        var memberships = await _db.TenantMemberships
            .Where(m => m.UserId == uid)
            .ToListAsync();
        var currentTenantId = memberships.FirstOrDefault()?.TenantId ?? user.TenantId;
        return new CurrentUser
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Memberships = memberships.Where(m => m.Status != "removed").Select(ToMembershipDto).ToList(),
            CurrentTenantId = currentTenantId,
        };
    }

    public override async Task<IDictionary<string, ICollection<EffectiveMenuNode>>> Menus()
    {
        // 2026-08-29: lab 后端调 saas /me/menus 是 server-to-server (无浏览器 cookie,
        // SameSite=Lax 跨域不带)。Bearer token 路径 (JwtBearer middleware 验签)
        // 必须可用,跟 Me() / Tenants() / Switch() 端点一致。保留 saas session
        // fallback 给浏览器 saas-vue/saas-react 调本端用 (cookie SameSite=Lax
        // 同站 / 直接 fetch 时浏览器带 cookie)。
        var uid = CurrentUserId();
        if (uid is null)
        {
            var session = _http.HttpContext?.Items[SaasSessionMiddleware.ItemsKey] as SaasSession;
            if (session is null)
                throw new UnauthorizedAccessException("saas session or Bearer token required for /me/menus");
            uid = session.UserId;
        }

        // Phase 5 占位：返回当前 tenant 所有 active menu（不接 role_menu_grants JOIN）
        var menus = await _db.Menus.Where(m => m.Status == "active").ToListAsync();
        var appIds = menus.Select(m => m.AppId).Distinct().ToList();
        var apps = await _db.Apps.Where(a => appIds.Contains(a.Id)).ToListAsync();
        var codeById = apps.ToDictionary(a => a.Id, a => a.Code);

        // 2026-08-29 修 prod 菜单树不显示: 之前每个 EffectiveMenuNode.Children 都赋
        // 空 List,父子关系没建立 → lab 端 MapSaasMenu 透传 children=[] → 前端
        // use-backend-menus 收到 flat 列表,children.length === 0 → 全判为 page →
        // 树形丢失 (分组节点应 type='group' 且 children=子菜单)。
        // 修: 一次性建 parent→children 映射,递归挂载;roots = ParentId == null。
        var byId = menus.ToDictionary(m => m.Id);
        var childrenByParent = menus
            .Where(m => m.ParentId.HasValue && byId.ContainsKey(m.ParentId.Value))
            .GroupBy(m => m.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        EffectiveMenuNode BuildNode(DbMenu m)
        {
            var dto = new EffectiveMenuNode
            {
                Id = m.Id,
                AppId = m.AppId,
                ParentId = m.ParentId ?? Guid.Empty,
                Code = m.Code,
                Name = m.Name,
                Path = m.Path,
                Icon = m.Icon,
                Type = m.Type switch
                {
                    "group" => MenuType.Group,
                    "page" => MenuType.Page,
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
            .Where(m => m.ParentId == null && codeById.ContainsKey(m.AppId))
            .Select(m => BuildNode(m))
            .GroupBy(n => codeById[n.AppId])
            .ToDictionary(g => g.Key, g => (ICollection<EffectiveMenuNode>)g.ToList());
        return grouped;
    }

    public override async Task<ICollection<ApiMembership>> Tenants()
    {
        var uid = CurrentUserId()
            ?? throw new UnauthorizedAccessException("no JWT sub claim");
        var memberships = await _db.TenantMemberships
            .Where(m => m.UserId == uid && m.Status != "removed")
            .ToListAsync();
        return memberships.Select(ToMembershipDto).ToList();
    }

    public override Task<SwitchTenantResponse> Switch(string tenantId)
    {
        var uid = CurrentUserId() ?? Guid.Empty;
        var tid = Guid.Parse(tenantId);
        return Task.FromResult(new SwitchTenantResponse
        {
            AccessToken = IssueAccessToken(uid, tid),
            RefreshToken = $"refresh-{uid}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            TenantId = tid,
        });
    }
}