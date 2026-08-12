using Saas.Identity.AspNetCore.Controllers.Generated;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Saas.Identity.AspNetCore.Tests")]

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// v0.2.0 后端 scaffold 用 in-memory fixture（所有 ID 用 Guid 匹配 NSwag 生成的 DTO）。
/// 真实存储未实现：所有 write 操作只更新内存 list，进程重启丢失。
/// 接入持久层时替换为 Service 注入即可，Controller 接口不动。
/// </summary>
internal static class InMemoryStore
{
    public static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid GlobexId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid AliceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid BobId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid AdminRoleId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid MemberRoleId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid LabAppId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static readonly Guid UsersMenuId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    public static readonly Guid RolesMenuId = Guid.Parse("40000000-0000-0000-0000-000000000002");
    public static readonly Guid ApiKeyId = Guid.Parse("50000000-0000-0000-0000-000000000001");

    public static readonly List<Tenant> Tenants = new()
    {
        new Tenant { Id = AcmeId,   Code = "acme",   Name = "ACME Corp", Status = TenantStatus.Active },
        new Tenant { Id = GlobexId, Code = "globex", Name = "Globex",     Status = TenantStatus.Active },
    };

    public static readonly List<User> Users = new()
    {
        new User { Id = AliceId, TenantId = AcmeId, Username = "alice", Email = "alice@acme.io", Status = UserStatus.Active,  RoleIds = new List<string> { AdminRoleId.ToString() } },
        new User { Id = BobId,   TenantId = AcmeId, Username = "bob",   Email = "bob@acme.io",   Status = UserStatus.Invited, RoleIds = new List<string>() },
    };

    public static readonly List<Role> Roles = new()
    {
        new Role { Id = AdminRoleId,  TenantId = AcmeId, Code = "admin",  Name = "管理员",   PermissionIds = new List<string> { "users.read", "users.write" } },
        new Role { Id = MemberRoleId, TenantId = AcmeId, Code = "member", Name = "普通成员", PermissionIds = new List<string> { "users.read" } },
    };

    public static readonly List<ApiKey> ApiKeys = new()
    {
        new ApiKey { Id = ApiKeyId, TenantId = AcmeId, Name = "Prod Key", Prefix = "sk_live", Status = ApiKeyStatus.Active },
    };

    public static readonly List<App> Apps = new()
    {
        new App { Id = LabAppId, Code = "lab-portal", Name = "实验室门户", Status = AppStatus.Active },
    };

    public static readonly List<Menu> Menus = new()
    {
        new Menu { Id = UsersMenuId, AppId = LabAppId, Code = "users", Name = "用户管理", Type = MenuType.Page, Status = MenuStatus.Active },
        new Menu { Id = RolesMenuId, AppId = LabAppId, Code = "roles", Name = "角色权限", Type = MenuType.Page, Status = MenuStatus.Active },
    };

    public static readonly List<RoleMenuGrant> RoleMenuGrants = new()
    {
        new RoleMenuGrant { RoleId = AdminRoleId, MenuIds = new List<string> { UsersMenuId.ToString(), RolesMenuId.ToString() }, UpdatedAt = DateTime.UtcNow },
    };

    public static readonly List<AuditEvent> AuditEvents = new()
    {
        new AuditEvent { Id = Guid.NewGuid(), TenantId = AcmeId, Action = AuditAction.User_created, ActorUserId = AliceId, OccurredAt = DateTime.UtcNow.AddDays(-1) },
        new AuditEvent { Id = Guid.NewGuid(), TenantId = AcmeId, Action = AuditAction.Login_success, ActorUserId = AliceId, OccurredAt = DateTime.UtcNow.AddHours(-2) },
    };

    /// <summary>
    /// 测试用：重置所有 in-memory 列表到 fixture 初始状态。
    /// 测试运行顺序不确定，必须每个 [Fact] 开头调一次避免相互污染。
    /// </summary>
    internal static void Reset()
    {
        Tenants.Clear();
        Tenants.Add(new Tenant { Id = AcmeId, Code = "acme", Name = "ACME Corp", Status = TenantStatus.Active });
        Tenants.Add(new Tenant { Id = GlobexId, Code = "globex", Name = "Globex", Status = TenantStatus.Active });

        Users.Clear();
        Users.Add(new User { Id = AliceId, TenantId = AcmeId, Username = "alice", Email = "alice@acme.io", Status = UserStatus.Active, RoleIds = new List<string> { AdminRoleId.ToString() } });
        Users.Add(new User { Id = BobId, TenantId = AcmeId, Username = "bob", Email = "bob@acme.io", Status = UserStatus.Invited, RoleIds = new List<string>() });

        Roles.Clear();
        Roles.Add(new Role { Id = AdminRoleId, TenantId = AcmeId, Code = "admin", Name = "管理员", PermissionIds = new List<string> { "users.read", "users.write" } });
        Roles.Add(new Role { Id = MemberRoleId, TenantId = AcmeId, Code = "member", Name = "普通成员", PermissionIds = new List<string> { "users.read" } });

        ApiKeys.Clear();
        ApiKeys.Add(new ApiKey { Id = ApiKeyId, TenantId = AcmeId, Name = "Prod Key", Prefix = "sk_live", Status = ApiKeyStatus.Active });

        Apps.Clear();
        Apps.Add(new App { Id = LabAppId, Code = "lab-portal", Name = "实验室门户", Status = AppStatus.Active });

        Menus.Clear();
        Menus.Add(new Menu { Id = UsersMenuId, AppId = LabAppId, Code = "users", Name = "用户管理", Type = MenuType.Page, Status = MenuStatus.Active });
        Menus.Add(new Menu { Id = RolesMenuId, AppId = LabAppId, Code = "roles", Name = "角色权限", Type = MenuType.Page, Status = MenuStatus.Active });

        RoleMenuGrants.Clear();
        RoleMenuGrants.Add(new RoleMenuGrant { RoleId = AdminRoleId, MenuIds = new List<string> { UsersMenuId.ToString(), RolesMenuId.ToString() }, UpdatedAt = DateTime.UtcNow });

        AuditEvents.Clear();
        AuditEvents.Add(new AuditEvent { Id = Guid.NewGuid(), TenantId = AcmeId, Action = AuditAction.User_created, ActorUserId = AliceId, OccurredAt = DateTime.UtcNow.AddDays(-1) });
        AuditEvents.Add(new AuditEvent { Id = Guid.NewGuid(), TenantId = AcmeId, Action = AuditAction.Login_success, ActorUserId = AliceId, OccurredAt = DateTime.UtcNow.AddHours(-2) });
    }
}