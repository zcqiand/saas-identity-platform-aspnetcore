namespace Saas.Identity.AspNetCore.Service;

/// <summary>
/// SCAFFOLD GET:/api/tenants/{tenantId}/users
/// SCAFFOLD POST:/api/tenants/{tenantId}/users
/// SCAFFOLD DELETE:/api/tenants/{tenantId}/users/{userId}
///
/// This service is HAND-WRITTEN business logic. Controllers come from codegen.
/// </summary>
public class TenantUsersService
{
    public class UserDto
    {
        public string Id { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public string Status { get; set; } = "active";
        public List<string> RoleIds { get; set; } = new();
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public long Total { get; set; }
    }

    public PagedResult<UserDto> ListUsers(string tenantId, int page, int pageSize, string? status)
    {
        // M01.F01.I01 — list users in tenant
        return new PagedResult<UserDto>
        {
            Items = new List<UserDto>
            {
                new UserDto
                {
                    Id = Guid.NewGuid().ToString(),
                    TenantId = tenantId,
                    Username = "alice",
                    Email = "alice@example.com",
                    Status = "active",
                },
            },
            Page = page,
            PageSize = pageSize,
            Total = 1,
        };
    }

    public UserDto CreateUser(string tenantId, string username, string email)
    {
        // M01.F01.I02 — create user in tenant
        return new UserDto
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            Username = username,
            Email = email,
            Status = "invited",
        };
    }

    public void DeleteUser(string tenantId, string userId)
    {
        // M01.F01.I05 — delete user in tenant
        // no-op for scaffold
    }
}