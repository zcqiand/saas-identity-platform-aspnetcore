using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbUser = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysUser;
using DbMember = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantMember;
using DbRole = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysRole;
using DtoTenantMemberView = Saas.Identity.AspNetCore.Controllers.Generated.TenantMemberView;
using DtoTenantMember = Saas.Identity.AspNetCore.Controllers.Generated.TenantMember;
using DtoSysUser = Saas.Identity.AspNetCore.Controllers.Generated.SysUser;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Services;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F02 租户成员 CRUD（v0.5.0 NSwag 重 emit 后：Tenants → Members）——
/// 从 TenantUsersController（User DTO）迁移到 TenantMembersController（TenantMemberView DTO）。
/// v0.5.0：9/7 重组 — sys_user 是 global 自然人，tenant-scoped 身份走 tenant_member 表。
/// 创建成员时同时建 SysUser + TenantMember 并绑初始角色。
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
        => ids == null ? new() : ids.Select(Guid.Parse).ToList();

    private static DtoTenantMemberView ToDto(DbMember m, DbUser u) => new()
    {
        // v0.5.0 NSwag 重 emit：TenantMemberView 形状变 { member: TenantMember, user: SysUser, roles: List<string> }
        Member = new DtoTenantMember
        {
            Id = m.Id,
            UserId = m.UserId,
            TenantId = m.TenantId,
            MemberName = m.MemberName,
            IsOwner = m.IsOwner,
            Status = (TenantMemberStatus)(int)m.Status,
            CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(m.CreatedAt, DateTimeKind.Utc)),
            UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(m.UpdatedAt, DateTimeKind.Utc)),
        },
        User = new DtoSysUser
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            Mobile = u.Mobile,
            Status = (SysUserStatus)(int)u.Status,
            FailedAttempts = u.FailedAttempts,
            LockedUntil = u.LockedUntil != null ? new DateTimeOffset(DateTime.SpecifyKind(u.LockedUntil.Value, DateTimeKind.Utc)) : default,
            CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(u.CreatedAt, DateTimeKind.Utc)),
            UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(u.UpdatedAt, DateTimeKind.Utc)),
        },
        Roles = m.Roles?.Select(r => r.Id.ToString()).ToList() ?? new List<string>(),
    };

    public override async Task<Response5> MembersGet(string tenantId, int? page, int? pageSize, TenantMemberStatus? status)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        var q = _db.TenantMembers.Where(m => m.TenantId == tid);
        if (status.HasValue) q = q.Where(m => m.Status == (short)status.Value);
        var items = await q.OrderByDescending(m => m.CreatedAt).Skip(p * ps).Take(ps).ToListAsync();
        var total = await q.CountAsync();
        return new Response5
        {
            Items = (await Task.WhenAll(items.Select(async m => ToDto(m, await _db.SysUsers.FirstOrDefaultAsync(u => u.Id == m.UserId) ?? new DbUser())))).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<DtoTenantMemberView> MembersPost(string tenantId, CreateSysUserRequest body)
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
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId); return ToDto(member, u);
    }

    public override async Task<DtoTenantMemberView> Invitations(string tenantId, Body3 body)
    {
        _guard.VerifyPathTenant(tenantId);
        var user = new DbUser
        {
            Id = Guid.NewGuid(),
            Username = body.Email ?? Guid.NewGuid().ToString(),
            Password = "",
            Email = body.Email,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.SysUsers.Add(user);
        var member = new DbMember
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse(tenantId),
            UserId = user.Id,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.TenantMembers.Add(member);
        await _db.SaveChangesAsync();
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId); return ToDto(member, u);
    }

    public override async Task<DtoTenantMemberView> MembersGet(string tenantId, string userId)
    {
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var member = await _db.TenantMembers.FirstOrDefaultAsync(m => m.UserId == uid && m.TenantId == Guid.Parse(tenantId))
            ?? throw new KeyNotFoundException("Member not found");
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId); return ToDto(member, u);
    }

    public override async Task<DtoTenantMemberView> MembersPatch(string tenantId, string userId, UpdateSysUserRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var member = await _db.TenantMembers.FirstOrDefaultAsync(m => m.UserId == uid && m.TenantId == Guid.Parse(tenantId))
            ?? throw new KeyNotFoundException("Member not found");
        // 9/7 重组：status 走 tenant_member.status（不再用 sys_user.status）
        member.Status = 1; // keep active; full status update goes through Status endpoint
        await _db.SaveChangesAsync();
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId); return ToDto(member, u);
    }

    public override async Task MembersDelete(string tenantId, string userId)
    {
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var member = await _db.TenantMembers.FirstOrDefaultAsync(m => m.UserId == uid && m.TenantId == Guid.Parse(tenantId));
        if (member != null)
        {
            _db.TenantMembers.Remove(member);
            await _db.SaveChangesAsync();
        }
    }

    public override async Task<DtoTenantMemberView> Roles(string tenantId, string userId, SetTenantMemberRolesRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var member = await _db.TenantMembers
            .Include(m => m.Roles)
            .FirstOrDefaultAsync(m => m.UserId == uid && m.TenantId == Guid.Parse(tenantId))
            ?? throw new KeyNotFoundException("Member not found");
        var roleIds = ParseRoleIds(body.RoleIds);
        member.Roles.Clear();
        if (roleIds.Count > 0)
        {
            var roles = await _db.SysRoles.Where(r => roleIds.Contains(r.Id)).ToListAsync();
            foreach (var r in roles) member.Roles.Add(r);
        }
        await _db.SaveChangesAsync();
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId); return ToDto(member, u);
    }

    public override async Task<DtoTenantMemberView> Status(string tenantId, string userId, Body4 body)
    {
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var member = await _db.TenantMembers.FirstOrDefaultAsync(m => m.UserId == uid && m.TenantId == Guid.Parse(tenantId))
            ?? throw new KeyNotFoundException("Member not found");
        member.Status = (short)body.Status;
        await _db.SaveChangesAsync();
        var u = await _db.SysUsers.FirstAsync(x => x.Id == member.UserId); return ToDto(member, u);
    }
}