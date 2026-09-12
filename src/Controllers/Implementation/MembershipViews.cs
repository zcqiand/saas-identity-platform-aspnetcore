using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbMember = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantMember;
using DtoMembership = Saas.Identity.AspNetCore.Controllers.Generated.TenantMembership;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// TenantMembership 扁平视图映射（ADR-0032 新模型 {id, userId, tenantId, roleIds, status, joinedAt}）。
/// 三个端点共用：login.availableTenants（AuthApi）、me.memberships（MeController.Me）、
/// GET /me/tenants（MeController.Tenants）。msw oracle：seed tenant_member.json 同形态，
/// roleIds 是 sys_role UUID 字符串数组，joinedAt = member.created_at。
/// </summary>
public static class MembershipViews
{
    private static DateTimeOffset Utc(DateTime db)
        => new(DateTime.SpecifyKind(db, DateTimeKind.Utc));

    public static DtoMembership FromEntity(DbMember m) => new()
    {
        Id = m.Id,
        UserId = m.UserId,
        TenantId = m.TenantId,
        // roleIds = tenant_member_role join 行**原样**返回，不按 sys_role.tenant_id 过滤。
        // 2026-09-12 四方 live（I03/I04）根因修复：msw oracle（seed tenant_member.json 逐字）
        // 与 nextjs 实测都把跨租户 assignment 原样吐出（seed 里 member c00000000006 @tenant2
        // 挂 role a00000000002，而该角色 tenant_id=tenant1），此前按 tenant_id 过滤把它吞成 []。
        RoleIds = m.Roles?
            .Select(r => r.Id.ToString())
            .ToList() ?? new List<string>(),
        Status = StatusEnumMaps.MapMemberStatus(m.Status),
        JoinedAt = Utc(m.CreatedAt),
    };

    public static List<DtoMembership> FromEntities(IEnumerable<DbMember> members)
        => members.Select(FromEntity).ToList();
}
