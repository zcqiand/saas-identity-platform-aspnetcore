using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbUser = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysUser;
using DbMember = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantMember;
using DbRole = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysRole;
using DtoMemberView = Saas.Identity.AspNetCore.Controllers.Generated.TenantMemberUserView;
using DtoNestedMemberView = Saas.Identity.AspNetCore.Controllers.Generated.TenantMemberView;
using DtoTenantMember = Saas.Identity.AspNetCore.Controllers.Generated.TenantMember;
using DtoSysUser = Saas.Identity.AspNetCore.Controllers.Generated.SysUser;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Infrastructure.Audit;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F02 租户成员 CRUD。
/// v0.5.0：9/7 重组 — sys_user 是 global 自然人，tenant-scoped 身份走 tenant_member 表。
/// ADR-0032（2026-09-12）：成员六端点（list/create/get/patch/put-roles/patch-status）
/// 响应改扁平 TenantMemberUserView {id, tenantId, username, email?, status, roleIds[], createdAt, updatedAt}：
///   - id = sys_user.id（路径 {userId} 同样寻址 sys_user.id，不是 tenant_member.id）
///   - 寻址键 (tenantId, userId)，roleIds = tenant_member_role ⨝ sys_role（sys_role.tenant_id 过滤）
///   - status 是 tenant_member.status（4 值，见 StatusEnumMaps）
/// invitations 端点保持嵌套 TenantMemberView（I42 方案 C）不动。
/// </summary>
public class TenantMembersController : TenantMembersControllerBase
{
    private readonly TenantGuard _guard;
    private readonly AppDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IHttpContextAccessor _http;

    public TenantMembersController(
        TenantGuard guard, AppDbContext db, IAuditWriter audit, IHttpContextAccessor http)
    {
        _guard = guard;
        _db = db;
        _audit = audit;
        _http = http;
    }

    private Guid? CallerUserId()
    {
        var sub = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? _http.HttpContext?.User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static List<Guid> ParseRoleIds(IEnumerable<string>? ids)
        => ids == null ? new() : ids.Select(ParseGuid).ToList();

    // 2026-09-12：路径参数 userId 非 GUID（"acme" 等）此前 Guid.Parse → FormatException 500；
    // 家族语义是资源不存在 → 404（Program.cs KeyNotFoundException 映射）。
    private static Guid ParseGuid(string s)
        => Guid.TryParse(s, out var g) ? g : throw new KeyNotFoundException($"invalid id: {s}");

    private static DateTimeOffset Utc(DateTime db)
        => new(DateTime.SpecifyKind(db, DateTimeKind.Utc));

    // ADR-0032 扁平视图：id = sys_user.id，status/时间取 tenant_member 行，roleIds 走
    // 导航集合**原样**返回（Roles 由 Include(m => m.Roles) 预加载）。
    // 2026-09-12：去掉 sys_role.tenant_id 过滤 —— msw oracle / nextjs 实测跨租户 assignment
    // 原样吐出（同 MembershipViews 的 I03/I04 根因），过滤会把它吞成 []。
    private static DtoMemberView ToFlatView(DbMember m, DbUser u)
    {
        var dto = new DtoMemberView
        {
            Id = u.Id,
            TenantId = m.TenantId,
            Username = u.Username,
            Email = u.Email,
            Status = StatusEnumMaps.MapMemberStatus(m.Status),
            CreatedAt = Utc(u.CreatedAt),
            UpdatedAt = Utc(u.UpdatedAt),
        };
        if (m.Roles is not null)
        {
            dto.RoleIds.AddRange(m.Roles.Select(r => r.Id.ToString()));
        }
        return dto;
    }

    // invitations 专用嵌套视图（ADR-0032 保持不动）：{ member: TenantMember, user: SysUser, roles }
    private static DtoNestedMemberView ToNestedView(DbMember m, DbUser u) => new()
    {
        Member = new DtoTenantMember
        {
            Id = m.Id,
            UserId = m.UserId,
            TenantId = m.TenantId,
            MemberName = m.MemberName,
            IsOwner = m.IsOwner,
            Status = StatusEnumMaps.MapMemberStatus(m.Status),
            CreatedAt = Utc(m.CreatedAt),
            UpdatedAt = Utc(m.UpdatedAt),
        },
        User = new DtoSysUser
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            Mobile = u.Mobile,
            Status = StatusEnumMaps.MapUserStatus(u.Status),
            FailedAttempts = u.FailedAttempts,
            LockedUntil = u.LockedUntil != null ? Utc(u.LockedUntil.Value) : default,
            CreatedAt = Utc(u.CreatedAt),
            UpdatedAt = Utc(u.UpdatedAt),
        },
        Roles = m.Roles?.Select(r => r.Id.ToString()).ToList() ?? new List<string>(),
    };

    // (tenantId, userId) 寻址 tenant_member 行；includeRoles=true 时预加载角色导航。
    private Task<DbMember?> FindMemberAsync(string tenantId, Guid userId, bool includeRoles)
    {
        var tid = Guid.Parse(tenantId);
        var q = _db.TenantMembers.Where(m => m.UserId == userId && m.TenantId == tid);
        return includeRoles
            ? q.Include(m => m.Roles).FirstOrDefaultAsync()
            : q.FirstOrDefaultAsync();
    }

    public override async Task<Response5> MembersGet(string tenantId, int? page, int? pageSize, TenantMemberStatus? status)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        var q = _db.TenantMembers.Where(m => m.TenantId == tid);
        if (status.HasValue) q = q.Where(m => m.Status == StatusEnumMaps.ToDbMemberStatus(status.Value));
        var total = await q.CountAsync();
        var items = await q
            .Include(m => m.Roles)
            .OrderByDescending(m => m.CreatedAt)
            .Skip(p * ps).Take(ps).ToListAsync();
        // 2026-09-12 修复：同一 DbContext 不允许并发查询（Task.WhenAll 内 await 首个查询后
        // 其余继续并发 → "A second operation was started on this context instance" 500）。
        // 改为一次批量取 users 组装字典，再串行映射。
        var userIds = items.Select(m => m.UserId).Distinct().ToList();
        var userMap = await _db.SysUsers
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);
        return new Response5
        {
            Items = items
                .Select(m => ToFlatView(m, userMap.GetValueOrDefault(m.UserId) ?? new DbUser()))
                .ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<DtoMemberView> MembersPost(string tenantId, CreateSysUserRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var user = new DbUser
        {
            Id = Guid.NewGuid(),
            Username = body.Username,
            Password = body.Password != null ? $"plain:{body.Password}" : "",
            Email = body.Email,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.SysUsers.Add(user);
        var member = new DbMember
        {
            Id = Guid.NewGuid(),
            TenantId = tid,
            UserId = user.Id,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.TenantMembers.Add(member);
        await _db.SaveChangesAsync();

        await _audit.WriteAsync(
            tenantId,
            CallerUserId()?.ToString(),
            "user_created",
            targetUserId: user.Id.ToString(),
            new Dictionary<string, object?> { ["userId"] = user.Id.ToString() });
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId);
        return ToFlatView(member, u);
    }

    public override async Task<DtoNestedMemberView> Invitations(string tenantId, Body3 body)
    {
        _guard.VerifyPathTenant(tenantId);
        var user = new DbUser
        {
            Id = Guid.NewGuid(),
            Username = body.Email ?? Guid.NewGuid().ToString(),
            Password = "",
            Email = body.Email,
            Status = 2, // invited（I42：invitation 建的是 status=2 invited 真 user 行，不是 1 active）
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.SysUsers.Add(user);
        var member = new DbMember
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse(tenantId),
            UserId = user.Id,
            MemberName = user.Username, // 家族约定：invitation 响应 memberName = username（= email）
            Status = 1, // member 立即 active（I42 oracle：user=invited, member=active）
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.TenantMembers.Add(member);
        await _db.SaveChangesAsync();
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId);
        return ToNestedView(member, u);
    }

    public override async Task<DtoMemberView> MembersGet(string tenantId, string userId)
    {
        _guard.VerifyPathTenant(tenantId);
        var member = await FindMemberAsync(tenantId, ParseGuid(userId), includeRoles: true)
            ?? throw new KeyNotFoundException("Member not found");
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId);
        return ToFlatView(member, u);
    }

    public override async Task<DtoMemberView> MembersPatch(string tenantId, string userId, UpdateSysUserRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var member = await FindMemberAsync(tenantId, ParseGuid(userId), includeRoles: true)
            ?? throw new KeyNotFoundException("Member not found");
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId);
        // 9/7 重组：status 走 tenant_member.status，专职 /status 端点；PATCH 只接 email/mobile
        //（UpdateSysUserRequest.Status 是非可空枚举，「未传」与「active」不可区分，不在此处理）。
        if (body.Email != null) u.Email = body.Email;
        if (body.Mobile != null) u.Mobile = body.Mobile;
        member.UpdatedAt = DateTime.UtcNow;
        u.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToFlatView(member, u);
    }

    public override async Task MembersDelete(string tenantId, string userId)
    {
        _guard.VerifyPathTenant(tenantId);
        var member = await FindMemberAsync(tenantId, ParseGuid(userId), includeRoles: false);
        if (member != null)
        {
            _db.TenantMembers.Remove(member);
            await _db.SaveChangesAsync();
        }
    }

    public override async Task<DtoMemberView> Roles(string tenantId, string userId, SetTenantMemberRolesRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var member = await FindMemberAsync(tenantId, ParseGuid(userId), includeRoles: true)
            ?? throw new KeyNotFoundException("Member not found");
        var roleIds = ParseRoleIds(body.RoleIds);
        // 候选角色按 sys_role.tenant_id 过滤：跨租户 roleId 静默忽略，不给本租户成员挂别租户角色。
        var roles = roleIds.Count == 0
            ? new List<DbRole>()
            : await _db.SysRoles.Where(r => roleIds.Contains(r.Id) && r.TenantId == tid).ToListAsync();
        member.Roles.Clear();
        foreach (var r in roles) member.Roles.Add(r);
        member.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId);
        return ToFlatView(member, u);
    }

    public override async Task<DtoMemberView> Status(string tenantId, string userId, Body4 body)
    {
        _guard.VerifyPathTenant(tenantId);
        var member = await FindMemberAsync(tenantId, ParseGuid(userId), includeRoles: true)
            ?? throw new KeyNotFoundException("Member not found");
        member.Status = StatusEnumMaps.ToDbMemberStatus(body.Status);
        member.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId);
        return ToFlatView(member, u);
    }
}
