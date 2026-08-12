namespace Saas.Identity.AspNetCore.Security;

/// <summary>
/// Verifies that a tenant-scoped Controller method's path tenantId
/// matches the JWT tenant_id claim. MANDATORY call at the start of
/// every tenant-scoped endpoint.
///
/// If you skip this guard, an attacker can pass any tenantId in the URL
/// and read another tenant's data — that's the whole point of this check.
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
        if (string.IsNullOrEmpty(pathTenantId) || pathTenantId != jwtTenantId)
        {
            throw new UnauthorizedAccessException(
                $"tenant mismatch: path={pathTenantId} jwt={jwtTenantId}");
        }
    }
}