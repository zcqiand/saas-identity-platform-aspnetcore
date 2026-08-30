using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using DbGrant = Saas.Identity.AspNetCore.Domain.Entities.RoleMenuGrant;
using Saas.Identity.AspNetCore.Security;

// alias 避免与 NSwag-generated DTO `RoleMenuGrant` 冲突
using RoleMenuGrantDto = Saas.Identity.AspNetCore.Controllers.Generated.RoleMenuGrant;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M09 角色菜单授权（tenant-scoped）。
/// v0.4.0：从 InMemoryStore 迁到 AppDbContext。
/// </summary>
public class TenantRoleMenusController : TenantRoleMenusControllerBase
{
    private readonly TenantGuard _guard;
    private readonly AppDbContext _db;

    public TenantRoleMenusController(TenantGuard guard, AppDbContext db)
    {
        _guard = guard;
        _db = db;
    }

    private static RoleMenuGrantDto ToDto(DbGrant e) => new()
    {
        RoleId = e.RoleId,
        TenantId = e.TenantId,
        MenuIds = (e.MenuIds ?? new()).Select(g => g.ToString()).ToList(),
        UpdatedAt = e.UpdatedAt,
    };

    public override async Task<RoleMenuGrantDto> MenusGet(string tenantId, string roleId)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var id = Guid.Parse(roleId);
        var row = await _db.RoleMenuGrants.FirstOrDefaultAsync(g => g.RoleId == id);
        if (row == null)
        {
            return new RoleMenuGrantDto
            {
                RoleId = id,
                TenantId = tid,
                MenuIds = new List<string>(),
                UpdatedAt = DateTimeOffset.UtcNow,
            };
        }
        return ToDto(row);
    }

    public override async Task<RoleMenuGrantDto> MenusPut(string tenantId, string roleId, SetRoleMenusRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var id = Guid.Parse(roleId);
        // 校验 role 存在
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role == null)
            throw new KeyNotFoundException($"role {roleId} not found");

        var menuIds = (body.MenuIds ?? new List<string>())
            .Where(m => Guid.TryParse(m, out _))
            .Select(m => Guid.Parse(m))
            .ToList();

        var existing = await _db.RoleMenuGrants.FirstOrDefaultAsync(g => g.RoleId == id);
        if (existing != null)
        {
            existing.MenuIds = menuIds;
            existing.TenantId = tid;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            _db.RoleMenuGrants.Add(new DbGrant
            {
                RoleId = id,
                TenantId = tid,
                MenuIds = menuIds,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        await _db.SaveChangesAsync();
        return new RoleMenuGrantDto
        {
            RoleId = id,
            TenantId = tid,
            MenuIds = menuIds.Select(g => g.ToString()).ToList(),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public override async Task MenusDelete(string tenantId, string roleId)
    {
        _guard.VerifyPathTenant(tenantId);
        var id = Guid.Parse(roleId);
        var row = await _db.RoleMenuGrants.FirstOrDefaultAsync(g => g.RoleId == id);
        if (row != null)
        {
            _db.RoleMenuGrants.Remove(row);
            await _db.SaveChangesAsync();
        }
    }
}