namespace Saas.Identity.AspNetCore.Security;

using Microsoft.Extensions.Hosting;

/// <summary>
/// Verifies that a tenant-scoped Controller method's path tenantId
/// matches the JWT tenant_id claim. MANDATORY call at the start of
/// every tenant-scoped endpoint.
///
/// If you skip this guard, an attacker can pass any tenantId in the URL
/// and read another tenant's data — that's the whole point of this check.
///
/// Dev fallback: 当 JWT 没认出来时（MSW/dev-helper 发的 alg=none token，
/// 或者 token 过期），dev 期间信任 path tenantId 让本地能跑通。
/// Production 仍按 JWT 严守，缺 claim 直接 throw。
/// </summary>
public class TenantGuard
{
    private readonly TenantContext _context;
    private readonly IHostEnvironment? _env;

    public TenantGuard(TenantContext context, IHostEnvironment? env = null)
    {
        _context = context;
        _env = env;
    }

    public void VerifyPathTenant(string pathTenantId)
    {
        var jwtTenantId = _context.CurrentTenantId();

        // Dev 兜底：JWT 没 claim 时信 path。MSW 发 alg=none dev token 当前无法过 JwtBearer 8。
        if (string.IsNullOrEmpty(jwtTenantId))
        {
            if (_env?.IsDevelopment() == true) return;
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