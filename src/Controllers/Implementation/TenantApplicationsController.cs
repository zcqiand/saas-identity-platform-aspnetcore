using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbApp = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.TenantApplication;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
// alias 避免与 NSwag-generated DTO `TenantApplication` 冲突
using ApiApp = Saas.Identity.AspNetCore.Controllers.Generated.TenantApplication;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F05 租户应用订阅 CRUD（guard-first：每个 op 第一行 VerifyPathTenant）。
/// 2026-09-10 审计 critical 补齐：Generated 基类早已生成但缺 concrete → DI 500。
/// clientId 按字符串列 oauth_clients.client_id 比对（非 UUID，与 ClientsController 同语义）。
/// </summary>
public class TenantApplicationsController : TenantApplicationsControllerBase
{
    private readonly AppDbContext _db;
    private readonly TenantGuard _guard;

    public TenantApplicationsController(AppDbContext db, TenantGuard guard)
    {
        _db = db;
        _guard = guard;
    }

    private static ApiApp ToDto(DbApp e) => new()
    {
        Id = e.Id,
        TenantId = e.TenantId,
        ClientId = e.ClientId,
        Status = e.Status,
        ExpireTime = new DateTimeOffset(DateTime.SpecifyKind(e.ExpireTime!.Value, DateTimeKind.Utc)),
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc)),
    };

    public override async Task<Response4> ApplicationsGet(string tenantId, int? page, int? pageSize)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        var items = await _db.TenantApplications
            .Where(ta => ta.TenantId == tid)
            .OrderByDescending(ta => ta.CreatedAt)
            .Skip(p * ps).Take(ps).ToListAsync();
        var total = await _db.TenantApplications.CountAsync(ta => ta.TenantId == tid);
        return new Response4
        {
            Items = items.Select(ToDto).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<ApiApp> ApplicationsPost(
        string tenantId, SubscribeTenantApplicationRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var tid = Guid.Parse(tenantId);
        // 无 (tenant_id, client_id) unique index —— 应用层查重，拒绝重复订阅
        //（否则 Patch/Delete 的 FirstOrDefaultAsync 会静默取任意一行）。
        // ArgumentException → 400 INVALID_REQUEST（Program.cs 异常映射段已映射；InvalidOperationException 未映射会落 500）。
        var duplicate = await _db.TenantApplications
            .AnyAsync(ta => ta.TenantId == tid && ta.ClientId == body.ClientId);
        if (duplicate)
        {
            throw new ArgumentException(
                $"subscription already exists: tenant={tenantId} client={body.ClientId}");
        }
        var client = await _db.OauthClients.FirstOrDefaultAsync(c => c.ClientId == body.ClientId)
            ?? throw new KeyNotFoundException($"client {body.ClientId} not found");
        var e = new DbApp
        {
            Id = Guid.NewGuid(),
            TenantId = tid,
            ClientId = body.ClientId,
            Status = 1, // active
            ExpireTime = body.ExpireTime.GetValueOrDefault().UtcDateTime,
            CreatedAt = DateTime.UtcNow,
        };
        _db.TenantApplications.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ApiApp> ApplicationsPatch(
        string tenantId, string clientId, UpdateTenantApplicationRequest body)
    {
        _guard.VerifyPathTenant(tenantId);
        var e = await _db.TenantApplications.FirstOrDefaultAsync(
                ta => ta.TenantId == Guid.Parse(tenantId) && ta.ClientId == clientId)
            ?? throw new KeyNotFoundException($"subscription tenant={tenantId} client={clientId} not found");
        e.Status = (short)body.Status;
        e.ExpireTime = body.ExpireTime.GetValueOrDefault().UtcDateTime;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task ApplicationsDelete(string tenantId, string clientId)
    {
        _guard.VerifyPathTenant(tenantId);
        var e = await _db.TenantApplications.FirstOrDefaultAsync(
                ta => ta.TenantId == Guid.Parse(tenantId) && ta.ClientId == clientId)
            ?? throw new KeyNotFoundException($"subscription tenant={tenantId} client={clientId} not found");
        _db.TenantApplications.Remove(e);
        await _db.SaveChangesAsync();
        Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
