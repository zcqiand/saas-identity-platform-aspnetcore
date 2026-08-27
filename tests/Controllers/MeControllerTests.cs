using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Security;
using Xunit;
using AppEntity = Saas.Identity.AspNetCore.Domain.Entities.App;
using UserEntity = Saas.Identity.AspNetCore.Domain.Entities.User;
using MenuEntity = Saas.Identity.AspNetCore.Domain.Entities.Menu;
using RoleMenuGrantEntity = Saas.Identity.AspNetCore.Domain.Entities.RoleMenuGrant;
using ApiKeyEntity = Saas.Identity.AspNetCore.Domain.Entities.ApiKey;
using AuditEventEntity = Saas.Identity.AspNetCore.Domain.Entities.AuditEvent;
using AuditRetentionPolicyEntity = Saas.Identity.AspNetCore.Domain.Entities.AuditRetentionPolicy;
using PermissionEntity = Saas.Identity.AspNetCore.Domain.Entities.Permission;
using RoleEntity = Saas.Identity.AspNetCore.Domain.Entities.Role;
using RolePermissionEntity = Saas.Identity.AspNetCore.Domain.Entities.RolePermission;
using TenantEntity = Saas.Identity.AspNetCore.Domain.Entities.Tenant;
using TenantMembershipEntity = Saas.Identity.AspNetCore.Domain.Entities.TenantMembership;

namespace Saas.Identity.AspNetCore.Tests.Controllers;

/// <summary>
/// M00.F02 当前用户身份 (whoami + 跨租户切换 + 我的菜单)。
/// TestAppDbContext: 跳过 PG 专属特性 (jsonb / native enum)，
/// 让 EF Core InMemory provider 能建 model。M00.F02 测试只关心
/// Users + Apps + Menus + RoleMenuGrants，其它 entity 直接 Ignore。
/// </summary>
internal class MeTestDbContext : AppDbContext
{
    public MeTestDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Ignore<ApiKeyEntity>();
        modelBuilder.Ignore<AuditEventEntity>();
        modelBuilder.Ignore<AuditRetentionPolicyEntity>();
        modelBuilder.Ignore<PermissionEntity>();
        modelBuilder.Ignore<RoleEntity>();
        modelBuilder.Ignore<RolePermissionEntity>();
        modelBuilder.Ignore<TenantEntity>();
        modelBuilder.Ignore<TenantMembershipEntity>();
    }
}

/// <summary>
/// M00.F02 当前用户身份（whoami + 跨租户切换 + 我的菜单）。
/// </summary>
public class MeControllerTests
{
    private static MeController BuildMeController(DefaultHttpContext? ctx = null, Guid? userId = null)
    {
        var dbOpts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"me-test-{Guid.NewGuid()}")
            .Options;
        var db = new MeTestDbContext(dbOpts);
        // Seed: 1 user + 1 app + 2 menus（与 seed 数据一致方便后续接 RoleMenuGrants）
        var uid = userId ?? Guid.NewGuid();
        var appId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        db.Apps.Add(new AppEntity
        {
            Id = appId, Code = "lab-management", Name = "lab-mgmt", Status = "active",
            ClientId = appId.ToString(),
            RedirectUris = new List<string> { "https://lab-vue.xiangru.uk/login" },
            Scopes = new List<string> { "lab.read" },
            GrantTypes = new List<string> { "authorization_code" },
            IsFirstParty = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(new UserEntity
        {
            Id = uid, TenantId = Guid.NewGuid(), Username = "alice", Email = "alice@acme.io",
            PasswordHash = "plain:x", Status = "active", CreatedAt = DateTimeOffset.UtcNow,
            DisplayName = "Alice",
        });
        db.Menus.Add(new MenuEntity
        {
            Id = Guid.NewGuid(), AppId = appId, Code = "m-dashboard", Name = "工作台",
            Path = "/", Type = "page", Status = "active",
            SortOrder = 1, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();

        var controller = new MeController(db, new HttpContextAccessor { HttpContext = ctx ?? new DefaultHttpContext() });
        return controller;
    }

    // === M09.F03.I01 me/menus session 校验 ===

    [Fact]
    [Trait("Fn", "M09.F03.I01")]
    public async Task Menus_noSession_throwsUnauthorized()
    {
        var c = BuildMeController(new DefaultHttpContext());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Menus());
    }

    [Fact]
    [Trait("Fn", "M09.F03.I01")]
    public async Task Menus_withValidSession_returnsDict()
    {
        var ctx = new DefaultHttpContext();
        var session = new SaasSession(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        ctx.Items[SaasSessionMiddleware.ItemsKey] = session;
        var c = BuildMeController(ctx);
        var menus = await c.Menus();
        Assert.NotEmpty(menus);
    }

    // === M00.F02 占位 Phase 5 ===

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M00.F02.I01")]
    public Task Whoami_returnsCurrentUser() => Task.CompletedTask;

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M00.F02.I02")]
    public Task Tenants_returnsMemberships() => Task.CompletedTask;

    [Fact(Skip = "M10.F04 集成测试留 Phase 5 Testcontainers PG")]
    [Trait("Fn", "M00.F02.I03")]
    public Task Switch_returnsNewToken() => Task.CompletedTask;
}