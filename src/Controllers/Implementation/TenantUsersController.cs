using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbUser = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysUser;
using DbMember = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantMember;
using DbRole = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysRole;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Services;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M01.F01 用户 CRUD（tenant-scoped via TenantMember）+ M01.F02 角色分配。
/// v0.4.0（M10.F04）：从 InMemoryStore 迁到 AppDbContext（DB-backed）。
/// v0.5.0：9/7 重组 — sys_user 是 global 自然人（无 TenantId / 无 RoleIds / 无 DisplayName），
/// tenant-scoped 身份走 tenant_member 表（TenantId / Roles skip nav / MemberName）。
/// 创建用户时同时建 SysUser + TenantMember 并绑定初始角色。
/// </summary>
public class TenantUsersController : TenantUsersControllerBase
{
    private readonly TenantGuard _guard;
    private readonly AppDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IHttpContextAccessor _http;

    public TenantUsersController(
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

    // === DTO ↔ Entity 转换 ===

    private static User ToDto(DbUser e) => new()
    {
        Id = e.Id,
        TenantId = Guid.Empty, // SysUser 无 TenantId（panorama §2.3 9/7 重组）；DTO 字段保留兼容占位
        Username = e.Username,
        Email = e.Email,
        DisplayName = e.Email, // SysUser 无 DisplayName；DTO 字段保留兼容占位
        Status = ToDtoStatus(e.Status),
        RoleIds = new List<string>(), // SysUser 无 RoleIds（auth 走 TenantMember.Roles）
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    private static UserStatus ToDtoStatus(short s) => s switch
    {
        2 => UserStatus.Suspended,
        3 => UserStatus.Disabled,
        1 => UserStatus.Active,
        _ => UserStatus.Invited,
    };

    private static short ToDbStatus(UserStatus s) => s switch
    {
        UserStatus.Active => 1,
        UserStatus.Suspended => 2,
        UserStatus.Disabled => 3,
        _ => 1,
    };

    private static List<Guid> ParseRoleIds(IEnumerable<string>? ids)
        => ids == null ? new() : ids.Select(Guid.Parse).ToList();

    /// <summary>查询某 tenant 下成员（join tenant_member）。</summary>
    private async Task<List<DbMember>> QueryMembers(Guid tenantId, UserStatus? status, int p, int ps)
    {
        var q = _db.TenantMembers.Include(m => m.Roles).Where(m => m.TenantId == tenantId);
        if (status.HasValue) q = q.Where(m => m.Status == ToDbStatus(status.Value));
        return await q.OrderByDescending(m => m.CreatedAt).Skip(p * ps).Take(ps).ToListAsync();
    }

    // === endpoints ===

    public override async Task<Response11> UsersGet(
        string tenantId, int? page, int? pageSize, UserStatus? status)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        var members = await QueryMembers(tid, status, p, ps);
        var total = await _db.TenantMembers.Where(m => m.TenantId == tid).CountAsync();
        // DTO 仍是 User 形状；TenantMemberView 在 SysUser 上叠 role + tenant context，
        // 当前实现简化为按 SysUser 投影（role/display 字段缺占位）
        var userIds = members.Select(m => m.UserId).Distinct().ToList();
        var users = await _db.SysUsers.Where(u => userIds.Contains(u.Id)).ToListAsync();
        var userById = users.ToDictionary(u => u.Id);
        var items = members.Select(m => ToDto(userById[m.UserId])).ToList();
        return new Response11
        {
            Items = items,
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<User> UsersPost(string tenantId, CreateUserRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        // 1. 创建 SysUser（global 自然人）
        var user = new DbUser
        {
            Id = Guid.NewGuid(),
            Username = body.Username,
            Password = body.Password != null ? $"plain:{body.Password}" : "",
            Email = body.Email,
            Status = 1, // active
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.SysUsers.Add(user);
        // 2. 创建 TenantMember（tenant-scoped 身份）
        var member = new DbMember
        {
            Id = Guid.NewGuid(),
            TenantId = tid,
            UserId = user.Id,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        // 3. 绑初始角色（如果 body 带 RoleIds）
        var roleIds = ParseRoleIds(body.RoleIds);
        if (roleIds.Count > 0)
        {
            member.Roles = await _db.SysRoles.Where(r => roleIds.Contains(r.Id)).ToListAsync();
        }
        _db.TenantMembers.Add(member);
        await _db.SaveChangesAsync();

        await _audit.WriteAsync(
            tenantId,
            CallerUserId()?.ToString(),
            "user_created",
            targetUserId: user.Id.ToString(),
            new Dictionary<string, object?> { ["userId"] = user.Id.ToString() });
        return ToDto(user);
    }

    public override async Task<User> Invitations(string tenantId, Body6 body)
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
        var roleIds = ParseRoleIds(body.RoleIds);
        if (roleIds.Count > 0)
        {
            member.Roles = await _db.SysRoles.Where(r => roleIds.Contains(r.Id)).ToListAsync();
        }
        _db.TenantMembers.Add(member);
        await _db.SaveChangesAsync();
        return ToDto(user);
    }

    public override async Task<User> UsersGet(string tenantId, string userId)
    {
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var entity = await _db.SysUsers.FirstOrDefaultAsync(u => u.Id == uid)
            ?? throw new KeyNotFoundException("User not found");
        return ToDto(entity);
    }

    public override async Task<User> UsersPatch(string tenantId, string userId, UpdateUserRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var entity = await _db.SysUsers.FirstOrDefaultAsync(u => u.Id == uid)
            ?? throw new KeyNotFoundException("User not found");
        if (body.Email != null) entity.Email = body.Email;
        entity.Status = ToDbStatus(body.Status);
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    public override async Task UsersDelete(string tenantId, string userId)
    {
        // 9/7 重组：删 tenant-scoped 身份不解 global account（删除 TenantMember 即可，
        // SysUser 保留供其他 tenant 复用）。
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var member = await _db.TenantMembers.FirstOrDefaultAsync(m => m.UserId == uid && m.TenantId == Guid.Parse(tenantId));
        if (member != null)
        {
            _db.TenantMembers.Remove(member);
            await _db.SaveChangesAsync();
        }
    }

    public override async Task<User> Roles(string tenantId, string userId, Body7 body)
    {
        // M01.F02.I01 分配角色 → 改写 tenant_member_role M:N（SysRole.Roles skip nav）
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var member = await _db.TenantMembers
            .Include(m => m.Roles)
            .FirstOrDefaultAsync(m => m.UserId == uid && m.TenantId == Guid.Parse(tenantId))
            ?? throw new KeyNotFoundException("User not found");
        var roleIds = ParseRoleIds(body.RoleIds);
        member.Roles.Clear();
        if (roleIds.Count > 0)
        {
            var roles = await _db.SysRoles.Where(r => roleIds.Contains(r.Id)).ToListAsync();
            foreach (var r in roles) member.Roles.Add(r);
        }
        await _db.SaveChangesAsync();
        var user = await _db.SysUsers.FirstAsync(u => u.Id == uid);
        return ToDto(user);
    }

    public override async Task<User> Status(string tenantId, string userId, Body8 body)
    {
        // 9/7 重组：status 走 tenant_member.status（membership 维度），不动 sys_user.status
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var member = await _db.TenantMembers.FirstOrDefaultAsync(m => m.UserId == uid && m.TenantId == Guid.Parse(tenantId))
            ?? throw new KeyNotFoundException("User not found");
        member.Status = ToDbStatus(body.Status);
        await _db.SaveChangesAsync();
        var user = await _db.SysUsers.FirstAsync(u => u.Id == uid);
        return ToDto(user);
    }
}