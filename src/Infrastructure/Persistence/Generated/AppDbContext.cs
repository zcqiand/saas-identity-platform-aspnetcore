using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Saas.Identity.AspNetCore.src.Infrastructure.Persistence.Generated;

public partial class AppDbContext : DbContext
{
    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ApiKey> ApiKeys { get; set; }

    public virtual DbSet<App> Apps { get; set; }

    public virtual DbSet<AuditEvent> AuditEvents { get; set; }

    public virtual DbSet<AuditRetentionPolicy> AuditRetentionPolicies { get; set; }

    public virtual DbSet<Menu> Menus { get; set; }

    public virtual DbSet<OauthCode> OauthCodes { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<RoleMenuGrant> RoleMenuGrants { get; set; }

    public virtual DbSet<RolePermission> RolePermissions { get; set; }

    public virtual DbSet<Tenant> Tenants { get; set; }

    public virtual DbSet<TenantMembership> TenantMemberships { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseNpgsql("Host=100.79.128.25;Port=5432;Database=saas_dev;Username=postgres;Password=qiand68+++");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum("api_key_status", new[] { "active", "revoked", "expired" })
            .HasPostgresEnum("app_status", new[] { "active", "disabled" })
            .HasPostgresEnum("audit_action", new[] { "user_created", "user_updated", "user_deleted", "role_assigned", "role_revoked", "login_success", "login_failed", "oauth_token_issued", "api_key_created", "api_key_revoked" })
            .HasPostgresEnum("membership_status", new[] { "active", "invited", "removed" })
            .HasPostgresEnum("menu_status", new[] { "active", "disabled" })
            .HasPostgresEnum("menu_type", new[] { "group", "page", "action" })
            .HasPostgresEnum("oauth_grant_type", new[] { "authorization_code", "refresh_token", "client_credentials", "password" })
            .HasPostgresEnum("tenant_status", new[] { "active", "suspended", "archived" })
            .HasPostgresEnum("user_status", new[] { "active", "invited", "suspended", "disabled" })
            .HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("api_keys_pkey");

            entity.ToTable("api_keys");

            entity.HasIndex(e => new { e.TenantId, e.Prefix }, "api_keys_tenant_prefix_unique").IsUnique();

            entity.HasIndex(e => e.ExpiresAt, "idx_api_keys_expires_at");

            entity.HasIndex(e => e.Prefix, "idx_api_keys_prefix_global");

            entity.HasIndex(e => e.TenantId, "idx_api_keys_tenant_id");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.Prefix)
                .HasMaxLength(16)
                .HasColumnName("prefix");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.Property(e => e.Scopes)
                .HasDefaultValueSql("ARRAY[]::text[]")
                .HasColumnName("scopes");
            entity.Property(e => e.SecretHash)
                .HasMaxLength(255)
                .HasColumnName("secret_hash");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");

            entity.HasOne(d => d.Tenant).WithMany(p => p.ApiKeys)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("api_keys_tenant_id_tenants_id_fk");
        });

        modelBuilder.Entity<App>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("apps_pkey");

            entity.ToTable("apps");

            entity.HasIndex(e => e.ClientId, "apps_client_id_unique").IsUnique();

            entity.HasIndex(e => e.Code, "apps_code_unique").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ClientId)
                .HasMaxLength(128)
                .HasColumnName("client_id");
            entity.Property(e => e.ClientSecretHash)
                .HasMaxLength(255)
                .HasColumnName("client_secret_hash");
            entity.Property(e => e.Code)
                .HasMaxLength(64)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Icon)
                .HasMaxLength(64)
                .HasColumnName("icon");
            entity.Property(e => e.IsFirstParty)
                .HasDefaultValue(false)
                .HasColumnName("is_first_party");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.RedirectUris)
                .HasDefaultValueSql("ARRAY[]::text[]")
                .HasColumnName("redirect_uris");
            entity.Property(e => e.Scopes)
                .HasDefaultValueSql("ARRAY[]::text[]")
                .HasColumnName("scopes");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0)
                .HasColumnName("sort_order");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("audit_events_pkey");

            entity.ToTable("audit_events");

            entity.HasIndex(e => e.ActorUserId, "idx_audit_events_actor");

            entity.HasIndex(e => e.Metadata, "idx_audit_events_metadata_gin").HasMethod("gin");

            entity.HasIndex(e => e.TargetUserId, "idx_audit_events_target");

            entity.HasIndex(e => new { e.TenantId, e.OccurredAt }, "idx_audit_events_tenant_occurred")
                .IsDescending(false, true)
                .HasNullSortOrder(new[] { NullSortOrder.NullsLast, NullSortOrder.NullsLast });

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(e => e.Metadata)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.OccurredAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("occurred_at");
            entity.Property(e => e.TargetUserId).HasColumnName("target_user_id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");

            entity.HasOne(d => d.ActorUser).WithMany(p => p.AuditEventActorUsers)
                .HasForeignKey(d => d.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("audit_events_actor_user_id_users_id_fk");

            entity.HasOne(d => d.TargetUser).WithMany(p => p.AuditEventTargetUsers)
                .HasForeignKey(d => d.TargetUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("audit_events_target_user_id_users_id_fk");

            entity.HasOne(d => d.Tenant).WithMany(p => p.AuditEvents)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("audit_events_tenant_id_tenants_id_fk");
        });

        modelBuilder.Entity<AuditRetentionPolicy>(entity =>
        {
            entity.HasKey(e => e.TenantId).HasName("audit_retention_policies_pkey");

            entity.ToTable("audit_retention_policies");

            entity.Property(e => e.TenantId)
                .ValueGeneratedNever()
                .HasColumnName("tenant_id");
            entity.Property(e => e.RetentionDays)
                .HasDefaultValue(90)
                .HasColumnName("retention_days");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Tenant).WithOne(p => p.AuditRetentionPolicy)
                .HasForeignKey<AuditRetentionPolicy>(d => d.TenantId)
                .HasConstraintName("audit_retention_policies_tenant_id_tenants_id_fk");
        });

        modelBuilder.Entity<Menu>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("menus_pkey");

            entity.ToTable("menus");

            entity.HasIndex(e => e.AppId, "idx_menus_app_id");

            entity.HasIndex(e => e.ParentId, "idx_menus_parent_id");

            entity.HasIndex(e => new { e.AppId, e.Code }, "menus_app_code_unique").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AppId).HasColumnName("app_id");
            entity.Property(e => e.Code)
                .HasMaxLength(64)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Icon)
                .HasMaxLength(64)
                .HasColumnName("icon");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.Path)
                .HasMaxLength(512)
                .HasColumnName("path");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0)
                .HasColumnName("sort_order");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.App).WithMany(p => p.Menus)
                .HasForeignKey(d => d.AppId)
                .HasConstraintName("menus_app_id_apps_id_fk");

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent)
                .HasForeignKey(d => d.ParentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("menus_parent_id_menus_id_fk");
        });

        modelBuilder.Entity<OauthCode>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("oauth_codes_pkey");

            entity.ToTable("oauth_codes");

            entity.HasIndex(e => e.AppId, "idx_oauth_codes_app_id");

            entity.HasIndex(e => e.ExpiresAt, "idx_oauth_codes_expires_at");

            entity.HasIndex(e => e.UserId, "idx_oauth_codes_user_id");

            entity.HasIndex(e => e.Code, "oauth_codes_code_unique").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AppId).HasColumnName("app_id");
            entity.Property(e => e.Code)
                .HasMaxLength(255)
                .HasColumnName("code");
            entity.Property(e => e.ConsumedAt).HasColumnName("consumed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.GrantType)
                .HasMaxLength(32)
                .HasDefaultValueSql("'authorization_code'::character varying")
                .HasColumnName("grant_type");
            entity.Property(e => e.RedirectUri)
                .HasMaxLength(2048)
                .HasColumnName("redirect_uri");
            entity.Property(e => e.Scope)
                .HasMaxLength(512)
                .HasColumnName("scope");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.App).WithMany(p => p.OauthCodes)
                .HasForeignKey(d => d.AppId)
                .HasConstraintName("oauth_codes_app_id_apps_id_fk");
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("permissions_pkey");

            entity.ToTable("permissions");

            entity.HasIndex(e => e.Code, "permissions_code_unique").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(128)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("roles_pkey");

            entity.ToTable("roles");

            entity.HasIndex(e => e.Code, "idx_roles_code_global");

            entity.HasIndex(e => e.TenantId, "idx_roles_tenant_id");

            entity.HasIndex(e => new { e.TenantId, e.Code }, "roles_tenant_code_unique").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(64)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Tenant).WithMany(p => p.Roles)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("roles_tenant_id_tenants_id_fk");
        });

        modelBuilder.Entity<RoleMenuGrant>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("role_menu_grants_pkey");

            entity.ToTable("role_menu_grants");

            entity.HasIndex(e => e.MenuIds, "idx_role_menu_grants_menu_ids_gin").HasMethod("gin");

            entity.HasIndex(e => e.TenantId, "idx_role_menu_grants_tenant_id");

            entity.Property(e => e.RoleId)
                .ValueGeneratedNever()
                .HasColumnName("role_id");
            entity.Property(e => e.MenuIds)
                .HasDefaultValueSql("ARRAY[]::uuid[]")
                .HasColumnName("menu_ids");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Role).WithOne(p => p.RoleMenuGrant)
                .HasForeignKey<RoleMenuGrant>(d => d.RoleId)
                .HasConstraintName("role_menu_grants_role_id_roles_id_fk");

            entity.HasOne(d => d.Tenant).WithMany(p => p.RoleMenuGrants)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("role_menu_grants_tenant_id_tenants_id_fk");
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(e => new { e.RoleId, e.PermissionId }).HasName("role_permissions_role_id_permission_id_pk");

            entity.ToTable("role_permissions");

            entity.HasIndex(e => e.PermissionId, "idx_role_permissions_permission_id");

            entity.HasIndex(e => e.RoleId, "idx_role_permissions_role_id");

            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.PermissionId).HasColumnName("permission_id");
            entity.Property(e => e.GrantedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("granted_at");

            entity.HasOne(d => d.Permission).WithMany(p => p.RolePermissions)
                .HasForeignKey(d => d.PermissionId)
                .HasConstraintName("role_permissions_permission_id_permissions_id_fk");

            entity.HasOne(d => d.Role).WithMany(p => p.RolePermissions)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("role_permissions_role_id_roles_id_fk");
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("tenants_pkey");

            entity.ToTable("tenants");

            entity.HasIndex(e => e.Code, "tenants_code_unique").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(64)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.Settings)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("settings");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<TenantMembership>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("tenant_memberships_pkey");

            entity.ToTable("tenant_memberships");

            entity.HasIndex(e => e.RoleIds, "idx_memberships_role_ids_gin").HasMethod("gin");

            entity.HasIndex(e => e.TenantId, "idx_memberships_tenant_id");

            entity.HasIndex(e => e.UserId, "idx_memberships_user_id");

            entity.HasIndex(e => new { e.UserId, e.TenantId }, "memberships_user_tenant_unique").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.JoinedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("joined_at");
            entity.Property(e => e.RoleIds)
                .HasDefaultValueSql("ARRAY[]::uuid[]")
                .HasColumnName("role_ids");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Tenant).WithMany(p => p.TenantMemberships)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("tenant_memberships_tenant_id_tenants_id_fk");

            entity.HasOne(d => d.User).WithMany(p => p.TenantMemberships)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("tenant_memberships_user_id_users_id_fk");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "idx_users_email_global");

            entity.HasIndex(e => e.TenantId, "idx_users_tenant_id");

            entity.HasIndex(e => new { e.TenantId, e.Email }, "users_tenant_email_unique").IsUnique();

            entity.HasIndex(e => new { e.TenantId, e.Username }, "users_tenant_username_unique").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.DisplayName)
                .HasMaxLength(255)
                .HasColumnName("display_name");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.RoleIds)
                .HasDefaultValueSql("ARRAY[]::uuid[]")
                .HasColumnName("role_ids");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.Username)
                .HasMaxLength(64)
                .HasColumnName("username");

            entity.HasOne(d => d.Tenant).WithMany(p => p.Users)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("users_tenant_id_tenants_id_fk");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
