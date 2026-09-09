using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;

public partial class AppDbContext : DbContext
{
    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<OauthAccessToken> OauthAccessTokens { get; set; }

    public virtual DbSet<OauthClient> OauthClients { get; set; }

    public virtual DbSet<OauthCode> OauthCodes { get; set; }

    public virtual DbSet<OauthRefreshToken> OauthRefreshTokens { get; set; }

    public virtual DbSet<SysMenu> SysMenus { get; set; }

    public virtual DbSet<SysRole> SysRoles { get; set; }

    public virtual DbSet<SysUser> SysUsers { get; set; }

    public virtual DbSet<Tenant> Tenants { get; set; }

    public virtual DbSet<TenantApplication> TenantApplications { get; set; }

    public virtual DbSet<TenantMember> TenantMembers { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // EF standard: when explicit options are already configured (test InMemory /
        // Program.cs DI NpgsqlDataSource), do not override. Only design-time tools
        // (scaffold) with no options fall through to env read + fail-fast below.
        if (optionsBuilder.IsConfigured) return;
        optionsBuilder.UseNpgsql(
            System.Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? throw new System.InvalidOperationException(
                    "DATABASE_URL 未设置。dev 加载 .env.test/.env.local；prod 由 deploy 脚本写入 VPS env-file。"
                    + "本规则遵循 CLAUDE.md §2 「禁止 env 默认值兜底」：secret 缺失必须 fail-fast。"
                    + "（runtime 走 Program.cs DI，不进 OnConfiguring；本 fallback 仅供 EF design-time 工具）"));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<OauthAccessToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("oauth_access_token_pkey");

            entity.ToTable("oauth_access_token");

            entity.HasIndex(e => e.ExpiresAt, "idx_access_token_expires");

            entity.HasIndex(e => new { e.UserId, e.TenantId }, "idx_access_token_user_tenant");

            entity.HasIndex(e => e.TokenId, "uk_access_token_id").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AccessToken).HasColumnName("access_token");
            entity.Property(e => e.ClientId)
                .HasMaxLength(64)
                .HasColumnName("client_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.Revoked)
                .HasDefaultValue(false)
                .HasColumnName("revoked");
            entity.Property(e => e.Scope)
                .HasMaxLength(255)
                .HasColumnName("scope");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.TokenId)
                .HasMaxLength(128)
                .HasColumnName("token_id");
            entity.Property(e => e.TokenType)
                .HasMaxLength(32)
                .HasDefaultValueSql("'Bearer'::character varying")
                .HasColumnName("token_type");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Client).WithMany(p => p.OauthAccessTokens)
                .HasPrincipalKey(p => p.ClientId)
                .HasForeignKey(d => d.ClientId)
                .HasConstraintName("oauth_access_token_client_id_oauth_client_client_id_fk");

            entity.HasOne(d => d.Tenant).WithMany(p => p.OauthAccessTokens)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("oauth_access_token_tenant_id_tenant_id_fk");

            entity.HasOne(d => d.User).WithMany(p => p.OauthAccessTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("oauth_access_token_user_id_sys_user_id_fk");
        });

        modelBuilder.Entity<OauthClient>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("oauth_client_pkey");

            entity.ToTable("oauth_client");

            entity.HasIndex(e => e.ClientId, "uk_oauth_client_id").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AccessTokenValidity)
                .HasDefaultValue(7200)
                .HasColumnName("access_token_validity");
            entity.Property(e => e.AutoApprove)
                .HasDefaultValue(false)
                .HasColumnName("auto_approve");
            entity.Property(e => e.ClientId)
                .HasMaxLength(64)
                .HasColumnName("client_id");
            entity.Property(e => e.ClientName)
                .HasMaxLength(128)
                .HasColumnName("client_name");
            entity.Property(e => e.ClientSecret)
                .HasMaxLength(255)
                .HasColumnName("client_secret");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.GrantTypes)
                .HasMaxLength(255)
                .HasColumnName("grant_types");
            entity.Property(e => e.RedirectUris).HasColumnName("redirect_uris");
            entity.Property(e => e.RefreshTokenValidity)
                .HasDefaultValue(2592000)
                .HasColumnName("refresh_token_validity");
            entity.Property(e => e.Scopes)
                .HasMaxLength(255)
                .HasColumnName("scopes");
            entity.Property(e => e.Status)
                .HasDefaultValue((short)1)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<OauthCode>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("oauth_code_pkey");

            entity.ToTable("oauth_code");

            entity.HasIndex(e => new { e.ClientId, e.UserId, e.TenantId }, "idx_oauth_code_client_user_tenant");

            entity.HasIndex(e => e.ExpiresAt, "idx_oauth_code_expires");

            entity.HasIndex(e => e.Code, "uk_oauth_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ClientId)
                .HasMaxLength(64)
                .HasColumnName("client_id");
            entity.Property(e => e.Code)
                .HasMaxLength(128)
                .HasColumnName("code");
            entity.Property(e => e.CodeChallenge)
                .HasMaxLength(128)
                .HasColumnName("code_challenge");
            entity.Property(e => e.CodeChallengeMethod)
                .HasMaxLength(16)
                .HasColumnName("code_challenge_method");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.RedirectUri)
                .HasMaxLength(500)
                .HasColumnName("redirect_uri");
            entity.Property(e => e.Scope)
                .HasMaxLength(255)
                .HasColumnName("scope");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Client).WithMany(p => p.OauthCodes)
                .HasPrincipalKey(p => p.ClientId)
                .HasForeignKey(d => d.ClientId)
                .HasConstraintName("oauth_code_client_id_oauth_client_client_id_fk");

            entity.HasOne(d => d.Tenant).WithMany(p => p.OauthCodes)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("oauth_code_tenant_id_tenant_id_fk");

            entity.HasOne(d => d.User).WithMany(p => p.OauthCodes)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("oauth_code_user_id_sys_user_id_fk");
        });

        modelBuilder.Entity<OauthRefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("oauth_refresh_token_pkey");

            entity.ToTable("oauth_refresh_token");

            entity.HasIndex(e => e.AccessTokenId, "idx_refresh_token_access_id");

            entity.HasIndex(e => new { e.UserId, e.TenantId }, "idx_refresh_token_user_tenant");

            entity.HasIndex(e => e.RefreshToken, "uk_refresh_token").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AccessTokenId).HasColumnName("access_token_id");
            entity.Property(e => e.ClientId)
                .HasMaxLength(64)
                .HasColumnName("client_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.RefreshToken)
                .HasMaxLength(128)
                .HasColumnName("refresh_token");
            entity.Property(e => e.Revoked)
                .HasDefaultValue(false)
                .HasColumnName("revoked");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.AccessToken).WithMany(p => p.OauthRefreshTokens)
                .HasForeignKey(d => d.AccessTokenId)
                .HasConstraintName("oauth_refresh_token_access_token_id_oauth_access_token_id_fk");

            entity.HasOne(d => d.Client).WithMany(p => p.OauthRefreshTokens)
                .HasPrincipalKey(p => p.ClientId)
                .HasForeignKey(d => d.ClientId)
                .HasConstraintName("oauth_refresh_token_client_id_oauth_client_client_id_fk");

            entity.HasOne(d => d.Tenant).WithMany(p => p.OauthRefreshTokens)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("oauth_refresh_token_tenant_id_tenant_id_fk");

            entity.HasOne(d => d.User).WithMany(p => p.OauthRefreshTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("oauth_refresh_token_user_id_sys_user_id_fk");
        });

        modelBuilder.Entity<SysMenu>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("sys_menu_pkey");

            entity.ToTable("sys_menu");

            entity.HasIndex(e => new { e.ClientId, e.ParentId }, "idx_sys_menu_client_parent");

            entity.HasIndex(e => new { e.ClientId, e.Type }, "idx_sys_menu_client_type");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ClientId)
                .HasMaxLength(64)
                .HasColumnName("client_id");
            entity.Property(e => e.Component)
                .HasMaxLength(255)
                .HasColumnName("component");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Icon)
                .HasMaxLength(128)
                .HasColumnName("icon");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.Path)
                .HasMaxLength(255)
                .HasColumnName("path");
            entity.Property(e => e.Perms)
                .HasMaxLength(128)
                .HasColumnName("perms");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0)
                .HasColumnName("sort_order");
            entity.Property(e => e.Status)
                .HasDefaultValue((short)1)
                .HasColumnName("status");
            entity.Property(e => e.Title)
                .HasMaxLength(64)
                .HasColumnName("title");
            entity.Property(e => e.Type).HasColumnName("type");

            entity.HasOne(d => d.Client).WithMany(p => p.SysMenus)
                .HasPrincipalKey(p => p.ClientId)
                .HasForeignKey(d => d.ClientId)
                .HasConstraintName("sys_menu_client_id_oauth_client_client_id_fk");
        });

        modelBuilder.Entity<SysRole>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("sys_role_pkey");

            entity.ToTable("sys_role");

            entity.HasIndex(e => new { e.TenantId, e.ClientId }, "idx_sys_role_tenant_client");

            entity.HasIndex(e => new { e.TenantId, e.ClientId, e.RoleCode }, "uk_tenant_client_role_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ClientId)
                .HasMaxLength(64)
                .HasColumnName("client_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.IsPreset)
                .HasDefaultValue(false)
                .HasColumnName("is_preset");
            entity.Property(e => e.RoleCode)
                .HasMaxLength(64)
                .HasColumnName("role_code");
            entity.Property(e => e.RoleName)
                .HasMaxLength(64)
                .HasColumnName("role_name");
            entity.Property(e => e.Status)
                .HasDefaultValue((short)1)
                .HasColumnName("status");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Client).WithMany(p => p.SysRoles)
                .HasPrincipalKey(p => p.ClientId)
                .HasForeignKey(d => d.ClientId)
                .HasConstraintName("sys_role_client_id_oauth_client_client_id_fk");

            entity.HasOne(d => d.Tenant).WithMany(p => p.SysRoles)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("sys_role_tenant_id_tenant_id_fk");

            entity.HasMany(d => d.Menus).WithMany(p => p.Roles)
                .UsingEntity<Dictionary<string, object>>(
                    "SysRoleMenu",
                    r => r.HasOne<SysMenu>().WithMany()
                        .HasForeignKey("MenuId")
                        .HasConstraintName("sys_role_menu_menu_id_sys_menu_id_fk"),
                    l => l.HasOne<SysRole>().WithMany()
                        .HasForeignKey("RoleId")
                        .HasConstraintName("sys_role_menu_role_id_sys_role_id_fk"),
                    j =>
                    {
                        j.HasKey("RoleId", "MenuId").HasName("sys_role_menu_role_id_menu_id_pk");
                        j.ToTable("sys_role_menu");
                        j.HasIndex(new[] { "MenuId" }, "idx_sys_role_menu_menu_id");
                        j.IndexerProperty<Guid>("RoleId").HasColumnName("role_id");
                        j.IndexerProperty<Guid>("MenuId").HasColumnName("menu_id");
                    });
        });

        modelBuilder.Entity<SysUser>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("sys_user_pkey");

            entity.ToTable("sys_user");

            entity.HasIndex(e => e.Email, "uk_sys_user_email").IsUnique();

            entity.HasIndex(e => e.Mobile, "uk_sys_user_mobile").IsUnique();

            entity.HasIndex(e => e.Username, "uk_sys_user_username").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(128)
                .HasColumnName("email");
            entity.Property(e => e.FailedAttempts)
                .HasDefaultValue(0)
                .HasColumnName("failed_attempts");
            entity.Property(e => e.LockedUntil).HasColumnName("locked_until");
            entity.Property(e => e.Mobile)
                .HasMaxLength(32)
                .HasColumnName("mobile");
            entity.Property(e => e.Password)
                .HasMaxLength(255)
                .HasColumnName("password");
            entity.Property(e => e.Status)
                .HasDefaultValue((short)1)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.Username)
                .HasMaxLength(64)
                .HasColumnName("username");
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("tenant_pkey");

            entity.ToTable("tenant");

            entity.HasIndex(e => e.TenantKey, "uk_tenant_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasDefaultValue((short)1)
                .HasColumnName("status");
            entity.Property(e => e.TenantKey)
                .HasMaxLength(64)
                .HasColumnName("tenant_key");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<TenantApplication>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("tenant_application_pkey");

            entity.ToTable("tenant_application");

            entity.HasIndex(e => e.ClientId, "idx_tenant_application_client_id");

            entity.HasIndex(e => new { e.TenantId, e.ClientId }, "uk_tenant_client").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.ClientId)
                .HasMaxLength(64)
                .HasColumnName("client_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpireTime).HasColumnName("expire_time");
            entity.Property(e => e.Status)
                .HasDefaultValue((short)1)
                .HasColumnName("status");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");

            entity.HasOne(d => d.Client).WithMany(p => p.TenantApplications)
                .HasPrincipalKey(p => p.ClientId)
                .HasForeignKey(d => d.ClientId)
                .HasConstraintName("tenant_application_client_id_oauth_client_client_id_fk");

            entity.HasOne(d => d.Tenant).WithMany(p => p.TenantApplications)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("tenant_application_tenant_id_tenant_id_fk");
        });

        modelBuilder.Entity<TenantMember>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("tenant_member_pkey");

            entity.ToTable("tenant_member");

            entity.HasIndex(e => e.TenantId, "idx_tenant_member_tenant_id");

            entity.HasIndex(e => e.UserId, "idx_tenant_member_user_id");

            entity.HasIndex(e => new { e.TenantId, e.UserId }, "uk_tenant_user").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("created_at");
            entity.Property(e => e.IsOwner)
                .HasDefaultValue(false)
                .HasColumnName("is_owner");
            entity.Property(e => e.MemberName)
                .HasMaxLength(64)
                .HasColumnName("member_name");
            entity.Property(e => e.Status)
                .HasDefaultValue((short)1)
                .HasColumnName("status");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Tenant).WithMany(p => p.TenantMembers)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("tenant_member_tenant_id_tenant_id_fk");

            entity.HasOne(d => d.User).WithMany(p => p.TenantMembers)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("tenant_member_user_id_sys_user_id_fk");

            entity.HasMany(d => d.Roles).WithMany(p => p.Members)
                .UsingEntity<Dictionary<string, object>>(
                    "TenantMemberRole",
                    r => r.HasOne<SysRole>().WithMany()
                        .HasForeignKey("RoleId")
                        .HasConstraintName("tenant_member_role_role_id_sys_role_id_fk"),
                    l => l.HasOne<TenantMember>().WithMany()
                        .HasForeignKey("MemberId")
                        .HasConstraintName("tenant_member_role_member_id_tenant_member_id_fk"),
                    j =>
                    {
                        j.HasKey("MemberId", "RoleId").HasName("tenant_member_role_member_id_role_id_pk");
                        j.ToTable("tenant_member_role");
                        j.HasIndex(new[] { "RoleId" }, "idx_tenant_member_role_role_id");
                        j.IndexerProperty<Guid>("MemberId").HasColumnName("member_id");
                        j.IndexerProperty<Guid>("RoleId").HasColumnName("role_id");
                    });
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
