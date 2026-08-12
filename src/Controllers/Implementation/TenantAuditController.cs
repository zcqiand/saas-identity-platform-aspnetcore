using Saas.Identity.AspNetCore.Security;
using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M06.F01 审计事件查询 + M06.F02 留存策略（tenant-scoped）。
/// </summary>
public class TenantAuditController : TenantAuditControllerBase
{
    private readonly TenantGuard _guard;
    public TenantAuditController(TenantGuard guard) { _guard = guard; }

    public override Task<Response5> AuditEvents(string tenantId, int? page, int? pageSize, string actorUserId, AuditAction? action, DateTimeOffset? from, DateTimeOffset? to)
    {
        // M06.F01.I01 审计事件列表
        _guard.VerifyPathTenant(tenantId);
        var gid = Guid.Parse(tenantId);
        var items = InMemoryStore.AuditEvents.Where(e => e.TenantId == gid).ToList();
        return Task.FromResult(new Response5
        {
            Items = items,
            Page = page ?? 1,
            PageSize = pageSize ?? items.Count,
            Total = items.Count,
        });
    }

    public override Task<Response6> ByUser(string tenantId, string userId, int? page, int? pageSize)
    {
        // M06.F01.I02 按用户查审计事件
        _guard.VerifyPathTenant(tenantId);
        var gid = Guid.Parse(tenantId);
        var uid = Guid.Parse(userId);
        var items = InMemoryStore.AuditEvents.Where(e => e.TenantId == gid && e.ActorUserId == uid).ToList();
        return Task.FromResult(new Response6
        {
            Items = items,
            Page = page ?? 1,
            PageSize = pageSize ?? items.Count,
            Total = items.Count,
        });
    }

    public override Task<Response7> Export(string tenantId, Body3 body)
    {
        // M06.F01.I03 导出审计事件（CSV）
        _guard.VerifyPathTenant(tenantId);
        return Task.FromResult(new Response7 { DownloadUrl = $"https://example.com/audit-export-{tenantId}.csv" });
    }

    public override Task<Response8> RetentionGet(string tenantId)
    {
        // M06.F02.I04 留存策略设置（GET）
        _guard.VerifyPathTenant(tenantId);
        return Task.FromResult(new Response8 { RetentionDays = 90 });
    }

    public override Task<Response9> RetentionPut(string tenantId, Body4 body)
    {
        // M06.F02.I04 留存策略设置（PUT）
        _guard.VerifyPathTenant(tenantId);
        return Task.FromResult(new Response9 { RetentionDays = body.RetentionDays });
    }
}