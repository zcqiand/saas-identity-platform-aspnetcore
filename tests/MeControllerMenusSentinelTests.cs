using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using Saas.Identity.AspNetCore.Security;
using DbSysMenu = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysMenu;
using DbSysRole = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysRole;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// 5.124 live 收口（5.112② 补漏）：/me/menus 根 sentinel parentId wire 序列化 null——
/// 2026-10-18 人批契约口径（tracker 5.112①，shared f2fcda8 regen）。
/// 5.112① 是两半：① regen DTO ParentId 翻 nullable（861bc89 已落）+
/// ② 根 sentinel 装配对齐（springboot 14254ad 已落）——本仓只落了①，
/// BuildNode 仍把 Guid.Empty 直灌 Guid? → wire 发 "00000000-…-0" 而非 null，
/// CT live me/menus 四方比对唯一分叉（其余三方全 null）。
/// </summary>
public class MeControllerMenusSentinelTests
{
    private static readonly Guid RootMenuId = new("00000000-0000-0000-0000-000000000101");

    private static (AppDbContext db, MeController ctrl) Make(string name, Guid uid, Guid roleId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name).Options;
        var db = new AppDbContext(options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JWT_SIGNING_KEY"] = "test-signing-key-0123456789abcdef0123456789",
            ["JWT_ISSUER"] = "test-issuer",
            ["JWT_AUDIENCE"] = "test-audience",
        }).Build();

        db.SysMenus.Add(new DbSysMenu
        {
            Id = RootMenuId,
            ClientId = "lab-management",
            ParentId = Guid.Empty,
            Title = "资源管理",
            Type = 1,
            SortOrder = 15,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
        });
        db.SysRoles.Add(new DbSysRole
        {
            Id = roleId,
            TenantId = Guid.NewGuid(),
            ClientId = "lab-management",
            RoleCode = "admin",
            RoleName = "Admin",
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
        // 角色菜单授权（sys_role_menu）：导航挂上，MeController 走 Include(r => r.Menus)
        db.SysRoles.First(r => r.Id == roleId).Menus.Add(db.SysMenus.First(m => m.Id == RootMenuId));

        db.SysUsers.Add(new SysUser
        {
            Id = uid,
            Username = "alice",
            Password = "pw",
            Email = "alice@acme.io",
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        var member = new TenantMember
        {
            Id = Guid.NewGuid(),
            UserId = uid,
            TenantId = Guid.NewGuid(),
            MemberName = "alice",
            IsOwner = false,
            Status = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.TenantMembers.Add(member);
        db.SaveChanges();
        member.Roles.Add(db.SysRoles.First(r => r.Id == roleId));
        db.SaveChanges();

        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            { new(ClaimTypes.NameIdentifier, uid.ToString()) }));
        return (db, new MeController(db, http, new JwtIssuer(config)));
    }

    [Fact]
    [Trait("Fn", "M04.F04.I08")]
    public async Task Menus_rootSentinel_parentIdSerializesNull_notZeroUuid()
    {
        var uid = Guid.NewGuid();
        var (db, ctrl) = Make("me-menus-sentinel", uid, Guid.NewGuid());
        try
        {
            var grouped = await ctrl.Menus("lab-management");
            Assert.True(grouped.TryGetValue("lab-management", out var nodes), "分组键应为 clientId");
            var root = Assert.Single(nodes);
            Assert.Equal(RootMenuId, root.Id);
            // 契约：根 sentinel（零值 UUID）wire 序列化 null（字段恒在值可空，5.112①）
            Assert.Null(root.ParentId);
        }
        finally
        {
            await db.DisposeAsync();
        }
    }
}
