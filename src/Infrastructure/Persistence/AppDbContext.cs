using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Domain.Entities;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence;

/// <summary>
/// AppDbContext — 镜像 saas-identity-platform-shared/sql/migrations/V001..V007。
///
/// 字段映射约定：
/// - snake_case column 名（用 EFCore.NamingConventions 兜底，这里 [Column] 显式声明）
/// - PG native enum 列映射为 string（migration 由 EF 自动 CREATE TYPE）
/// - JSONB 列映射为 Dictionary&lt;string, object?&gt;
/// - 数组列映射为 List&lt;T&gt;（Npgsql 原生支持）
///
/// ADR-0010：OnModelCreating 严格镜像 shared SQL；CI 钩子 scripts/check-ef-mirrors-sql.sh
/// 强制 EF migration script 与 shared SQL 字段级 diff=0。
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<App> Apps => Set<App>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<RoleMenuGrant> RoleMenuGrants => Set<RoleMenuGrant>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AuditRetentionPolicy> AuditRetentionPolicies => Set<AuditRetentionPolicy>();
    public DbSet<OauthCode> OauthCodes => Set<OauthCode>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // === tenants ===
        b.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedOnAdd();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("tenant_status")
                .HasConversion(s => Enum.Parse<TenantStatusPg>(s),
                               pg => pg.ToString())
                .IsRequired();
            e.Property(x => x.Settings).HasColumnName("settings").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("tenants_code_unique");
        });

        // === users ===
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedOnAdd();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").HasColumnType("uuid").IsRequired();
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(255);
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("user_status")
                .HasConversion(s => Enum.Parse<UserStatusPg>(s),
                               pg => pg.ToString())
                .IsRequired();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(255);
            e.Property(x => x.RoleIds).HasColumnName("role_ids").HasColumnType("uuid[]").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique().HasDatabaseName("users_tenant_email_unique");
            e.HasIndex(x => new { x.TenantId, x.Username }).IsUnique().HasDatabaseName("users_tenant_username_unique");
        });

        // === tenant_memberships ===
        b.Entity<TenantMembership>(e =>
        {
            e.ToTable("tenant_memberships");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedOnAdd();
            e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid").IsRequired();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").HasColumnType("uuid").IsRequired();
            e.Property(x => x.RoleIds).HasColumnName("role_ids").HasColumnType("uuid[]").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("membership_status")
                .HasConversion(s => Enum.Parse<MembershipStatusPg>(s),
                               pg => pg.ToString())
                .IsRequired();
            e.Property(x => x.JoinedAt).HasColumnName("joined_at").HasColumnType("timestamptz").IsRequired();
            e.HasIndex(x => new { x.UserId, x.TenantId }).IsUnique().HasDatabaseName("memberships_user_tenant_unique");
        });

        // === roles ===
        b.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedOnAdd();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").HasColumnType("uuid").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("roles_tenant_code_unique");
        });

        // === permissions ===
        b.Entity<Permission>(e =>
        {
            e.ToTable("permissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedOnAdd();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(128).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("permissions_code_unique");
        });

        // === role_permissions（复合主键） ===
        b.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions");
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.Property(x => x.RoleId).HasColumnName("role_id").HasColumnType("uuid");
            e.Property(x => x.PermissionId).HasColumnName("permission_id").HasColumnType("uuid");
            e.Property(x => x.GrantedAt).HasColumnName("granted_at").HasColumnType("timestamptz").IsRequired();
        });

        // === api_keys ===
        b.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedOnAdd();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").HasColumnType("uuid").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            e.Property(x => x.Prefix).HasColumnName("prefix").HasMaxLength(16).IsRequired();
            e.Property(x => x.SecretHash).HasColumnName("secret_hash").HasMaxLength(255).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("api_key_status")
                .HasConversion(s => Enum.Parse<ApiKeyStatusPg>(s),
                               pg => pg.ToString())
                .IsRequired();
            e.Property(x => x.Scopes).HasColumnName("scopes").HasColumnType("text[]").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            e.Property(x => x.LastUsedAt).HasColumnName("last_used_at").HasColumnType("timestamptz");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz");
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamptz");
            e.HasIndex(x => new { x.TenantId, x.Prefix }).IsUnique().HasDatabaseName("api_keys_tenant_prefix_unique");
        });

        // === apps ===
        b.Entity<App>(e =>
        {
            e.ToTable("apps");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedOnAdd();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Icon).HasColumnName("icon").HasMaxLength(64);
            e.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("app_status").IsRequired();
            e.Property(x => x.ClientId).HasColumnName("client_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.ClientSecretHash).HasColumnName("client_secret_hash").HasMaxLength(255);
            e.Property(x => x.RedirectUris).HasColumnName("redirect_uris").HasColumnType("text[]").IsRequired();
            e.Property(x => x.Scopes).HasColumnName("scopes").HasColumnType("text[]").IsRequired();
            e.Property(x => x.GrantTypes).HasColumnName("grant_types").HasColumnType("oauth_grant_type[]").IsRequired();
            e.Property(x => x.IsFirstParty).HasColumnName("is_first_party").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("apps_code_unique");
            e.HasIndex(x => x.ClientId).IsUnique().HasDatabaseName("apps_client_id_unique");
        });

        // === menus ===
        b.Entity<Menu>(e =>
        {
            e.ToTable("menus");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedOnAdd();
            e.Property(x => x.AppId).HasColumnName("app_id").HasColumnType("uuid").IsRequired();
            e.Property(x => x.ParentId).HasColumnName("parent_id").HasColumnType("uuid");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            e.Property(x => x.Path).HasColumnName("path").HasMaxLength(512);
            e.Property(x => x.Icon).HasColumnName("icon").HasMaxLength(64);
            e.Property(x => x.Type).HasColumnName("type").HasColumnType("menu_type").IsRequired();
            e.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasColumnType("menu_status").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
            e.HasIndex(x => new { x.AppId, x.Code }).IsUnique().HasDatabaseName("menus_app_code_unique");
        });

        // === role_menu_grants（role_id PK） ===
        b.Entity<RoleMenuGrant>(e =>
        {
            e.ToTable("role_menu_grants");
            e.HasKey(x => x.RoleId);
            e.Property(x => x.RoleId).HasColumnName("role_id").HasColumnType("uuid");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").HasColumnType("uuid").IsRequired();
            e.Property(x => x.MenuIds).HasColumnName("menu_ids").HasColumnType("uuid[]").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
        });

        // === audit_events ===
        b.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedOnAdd();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").HasColumnType("uuid").IsRequired();
            e.Property(x => x.ActorUserId).HasColumnName("actor_user_id").HasColumnType("uuid");
            e.Property(x => x.Action).HasColumnName("action").HasColumnType("audit_action")
                .HasConversion(a => Enum.Parse<AuditActionPg>(a),
                               pg => pg.ToString())
                .IsRequired();
            e.Property(x => x.TargetUserId).HasColumnName("target_user_id").HasColumnType("uuid");
            e.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamptz").IsRequired();
        });

        // === audit_retention_policies（tenant_id PK） ===
        b.Entity<AuditRetentionPolicy>(e =>
        {
            e.ToTable("audit_retention_policies");
            e.HasKey(x => x.TenantId);
            e.Property(x => x.TenantId).HasColumnName("tenant_id").HasColumnType("uuid");
            e.Property(x => x.RetentionDays).HasColumnName("retention_days").IsRequired();
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
        });

        // === oauth_codes (V014) — Phase 6 OAuth 2.0 authorization_code + refresh_token 存储 ===
        // 镜像 shared/sql/migrations/V014__seed_lab_mgmt_app.sql；
        // 替代 saas-nextjs 进程内 oauth-store, 3 个 saas 后端共用同一持久化 schema。
        b.Entity<OauthCode>(e =>
        {
            e.ToTable("oauth_codes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedOnAdd();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(255).IsRequired();
            e.Property(x => x.GrantType).HasColumnName("grant_type").HasMaxLength(32).IsRequired();
            e.Property(x => x.AppId).HasColumnName("app_id").HasColumnType("uuid").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").HasColumnType("uuid").IsRequired();
            e.Property(x => x.RedirectUri).HasColumnName("redirect_uri").HasMaxLength(2048);
            e.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(512);
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired();
            e.Property(x => x.ConsumedAt).HasColumnName("consumed_at").HasColumnType("timestamptz");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("oauth_codes_code_unique");
            e.HasIndex(x => x.AppId).HasDatabaseName("idx_oauth_codes_app_id");
            e.HasIndex(x => x.ExpiresAt).HasDatabaseName("idx_oauth_codes_expires_at");
            e.HasIndex(x => x.UserId).HasDatabaseName("idx_oauth_codes_user_id");
        });
    }
}