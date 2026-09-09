namespace Saas.Identity.AspNetCore.Security;

/// <summary>
/// Verifies that a tenant-scoped Controller method's path tenantId
/// matches the JWT tenant_id claim. MANDATORY call at the start of
/// every tenant-scoped endpoint.
///
/// If you skip this guard, an attacker can pass any tenantId in the URL
/// and read another tenant's data — that's the whole point of this check.
///
/// ADR-0019：JWT 缺 tenant_id claim 一律 401（throw），无 dev 兜底。
/// JwtBearer v0.2.1 起已拒 alg=none dev token，缺 claim 的请求到不了这里——
/// 旧 dev 分支（2026-09-10 审计红线 #3）已删。
/// </summary>
public class TenantGuard
{
    private readonly TenantContext _context;

    public TenantGuard(TenantContext context)
    {
        _context = context;
    }

    public void VerifyPathTenant(string pathTenantId)
    {
        var jwtTenantId = _context.CurrentTenantId();

        if (string.IsNullOrEmpty(jwtTenantId))
        {
            throw new UnauthorizedAccessException(
                $"tenant mismatch: path={pathTenantId} jwt={jwtTenantId}");
        }

        if (pathTenantId != jwtTenantId)
        {
            throw new UnauthorizedAccessException(
                $"tenant mismatch: path={pathTenantId} jwt={jwtTenantId}");
        }
    }
}
