using System.Security.Claims;

namespace Saas.Identity.AspNetCore.Security;

/// <summary>
/// Holds the current tenant_id from the authenticated user's JWT claims.
/// Used by TenantGuard to verify path-carried tenantId matches JWT claim.
///
/// Virtual methods so tests can subclass with stubs.
/// </summary>
public class TenantContext
{
    private readonly IHttpContextAccessor? _accessor;

    public TenantContext() : this(null) { }

    public TenantContext(IHttpContextAccessor? accessor)
    {
        _accessor = accessor;
    }

    public virtual string? CurrentTenantId()
    {
        var user = _accessor?.HttpContext?.User;
        return user?.FindFirst("tenant_id")?.Value;
    }

    public virtual string? CurrentUserId()
    {
        var user = _accessor?.HttpContext?.User;
        return user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? user?.FindFirst("sub")?.Value;
    }
}