using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Services;
// alias to disambiguate from NSwag-generated DTO `User`
using DbUser = Saas.Identity.AspNetCore.Domain.Entities.User;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M01.F01 用户 CRUD（tenant-scoped）+ M01.F02 角色分配/状态切换/邀请。
/// v0.4.0（M10.F04）：从 InMemoryStore 迁到 AppDbContext（DB-backed）。
///
/// 边界规则：
/// - 每个 tenant-scoped 方法第一行调 _tenantGuard.VerifyPathTenant(tenantId)
/// - DbContext 通过构造器注入（CLAUDE.md §2 禁止字段注入）
/// - DTO ↔ Entity 转换在 Controller 内手写（本仓不引 AutoMapper）
/// - InMemoryStore 保留作为 fixture 给单元测试用；运行时改走 DB
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

    // 同 TenantApiKeysController.CallerUserId：JWT sub claim → actor（解析失败 null = 系统动作）
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
        TenantId = e.TenantId,
        Username = e.Username,
        Email = e.Email,
        DisplayName = e.DisplayName,
        Status = ToDtoStatus(e.Status),
        RoleIds = e.RoleIds.Select(g => g.ToString()).ToList(),
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    private static UserStatus ToDtoStatus(string s) => s switch
    {
        "active" => UserStatus.Active,
        "invited" => UserStatus.Invited,
        "suspended" => UserStatus.Suspended,
        "disabled" => UserStatus.Disabled,
        _ => UserStatus.Invited,
    };

    private static string ToDbStatus(UserStatus s) => s switch
    {
        UserStatus.Active => "active",
        UserStatus.Invited => "invited",
        UserStatus.Suspended => "suspended",
        UserStatus.Disabled => "disabled",
        _ => "invited",
    };

    private static List<Guid> ParseRoleIds(IEnumerable<string>? ids)
        => ids == null ? new() : ids.Select(Guid.Parse).ToList();

    // === endpoints ===

    public override async Task<Response11> UsersGet(
        string tenantId, int? page, int? pageSize, UserStatus? status)
    {
        // M01.F01.I01 用户列表（tenant-scoped）
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        // 2026-08-30 contract-test：其他 3 后端 0-indexed（Spring Data PageRequest 约定），aspnetcore 改为 0-indexed 对齐
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        var q = _db.Users.Where(u => u.TenantId == tid);
        if (status.HasValue) q = q.Where(u => u.Status == ToDbStatus(status.Value));
        var items = await q.OrderByDescending(u => u.CreatedAt)
            .Skip(p * ps).Take(ps).ToListAsync();
        var total = await q.CountAsync();
        return new Response11
        {
            Items = items.Select(ToDto).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<User> UsersPost(string tenantId, CreateUserRequest body)
    {
        // M01.F01.I02 创建用户（CreateUserRequest 不含 status，初始为 Invited）
        _guard.VerifyPathTenant(tenantId);
        var entity = new DbUser
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse(tenantId),
            Username = body.Username,
            Email = body.Email,
            DisplayName = body.DisplayName,
            Status = "active",
            RoleIds = ParseRoleIds(body.RoleIds),
            // Phase 5：换 argon2.hash(body.Password)
            PasswordHash = body.Password != null ? $"plain:{body.Password}" : null,
            // 2026-09-01 contract-test M96.F02.I19：补 CreatedAt/UpdatedAt，
            // 与 AdminTenantsController.TenantsPost (line 116-117) 对齐。
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Users.Add(entity);
        await _db.SaveChangesAsync();
        // M01.F01.I02 写端点副作用 — user_created（2026-09-02 contract-test M96 audit 覆盖对齐，
        // 形状对齐 nextjs/springboot：metadata={userId}；caller 为 null 时系统动作语义）
        await _audit.WriteAsync(
            tenantId,
            CallerUserId()?.ToString(),
            "user_created",
            targetUserId: entity.Id.ToString(),
            new Dictionary<string, object?> { ["userId"] = entity.Id.ToString() });
        return ToDto(entity);
    }

    public override async Task<User> Invitations(string tenantId, Body6 body)
    {
        // M01.F02.I02 邀请用户
        _guard.VerifyPathTenant(tenantId);
        var entity = new DbUser
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.Parse(tenantId),
            Username = body.Email,
            Email = body.Email,
            Status = "invited",
            RoleIds = ParseRoleIds(body.RoleIds),
            // 2026-09-01 contract-test M96.F02.I19：补 CreatedAt/UpdatedAt。
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Users.Add(entity);
        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    public override async Task<User> UsersGet(string tenantId, string userId)
    {
        // M01.F01.I03 用户详情
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var entity = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid)
            ?? throw new KeyNotFoundException($"User not found"); ;
        return ToDto(entity);
    }

    public override async Task<User> UsersPatch(string tenantId, string userId, UpdateUserRequest body)
    {
        // M01.F01.I04 更新用户
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var entity = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid)
            ?? throw new KeyNotFoundException($"User not found"); ;
        if (body.Email != null) entity.Email = body.Email;
        if (body.DisplayName != null) entity.DisplayName = body.DisplayName;
        entity.Status = ToDbStatus(body.Status);
        if (body.RoleIds != null) entity.RoleIds = ParseRoleIds(body.RoleIds);
        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    public override async Task UsersDelete(string tenantId, string userId)
    {
        // M01.F01.I05 删除用户
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var entity = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
        if (entity != null)
        {
            _db.Users.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public override async Task<User> Roles(string tenantId, string userId, Body7 body)
    {
        // M01.F01.I06 / M01.F02.I01 分配角色
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var entity = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid)
            ?? throw new KeyNotFoundException($"User not found"); ;
        entity.RoleIds = ParseRoleIds(body.RoleIds);
        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    public override async Task<User> Status(string tenantId, string userId, Body8 body)
    {
        // M01.F02.I03 状态切换
        _guard.VerifyPathTenant(tenantId);
        var uid = Guid.Parse(userId);
        var entity = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid)
            ?? throw new KeyNotFoundException($"User not found"); ;
        entity.Status = ToDbStatus(body.Status);
        await _db.SaveChangesAsync();
        return ToDto(entity);
    }
}