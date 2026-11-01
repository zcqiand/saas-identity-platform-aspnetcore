using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Implementation;
using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbSysMenu = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.SysMenu;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests;

/// <summary>
/// 5.121 家族裁定：菜单列表确定性——sortOrder 主键 + id 次级键。
/// 修前 MenusGet 完全无 ORDER BY（InMemory 按插入序吐，逆序插入必红）、
/// Reorder 用 ThenBy(Title)（家族错位：别家按 id；固定 Guid + 反排 Title 必红）。
/// </summary>
public class ClientMenusControllerTests
{
    private static readonly Guid FixedLate = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid FixedEarly = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid[] FixedTied =
    {
        new("00000000-0000-0000-0000-000000000011"),
        new("00000000-0000-0000-0000-000000000012"),
        new("00000000-0000-0000-0000-000000000013"),
        new("00000000-0000-0000-0000-000000000014"),
        new("00000000-0000-0000-0000-000000000015"),
        new("00000000-0000-0000-0000-000000000016"),
        new("00000000-0000-0000-0000-000000000017"),
        new("00000000-0000-0000-0000-000000000018"),
    };

    private static DbSysMenu Menu(Guid id, string title, int sortOrder) => new()
    {
        Id = id,
        ClientId = "order-test",
        ParentId = Guid.Empty,
        Title = title,
        Type = 1,
        SortOrder = sortOrder,
        Status = 1,
        CreatedAt = DateTime.UtcNow,
    };

    private static (AppDbContext db, ClientMenusController ctrl) Make(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName).Options;
        var db = new AppDbContext(options);
        var ctrl = new ClientMenusController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (db, ctrl);
    }

    [Fact]
    [Trait("Fn", "M04.F04.I01")]
    public async Task MenusGet_ordersBySortOrderThenId()
    {
        var (db, ctrl) = Make("client-menus-list");
        // 逆序插入：early(2) 先、late(1) 后、并列组 id 降序——无 ORDER BY 时 InMemory
        // 按插入序吐，主键/次级键双红（确定性，非 heap 概率）
        db.SysMenus.Add(Menu(FixedEarly, "early", 2));
        db.SysMenus.Add(Menu(FixedLate, "late", 1));
        for (var i = FixedTied.Length - 1; i >= 0; i--)
        {
            db.SysMenus.Add(Menu(FixedTied[i], $"tied-{i}", 5));
        }

        await db.SaveChangesAsync();
        var rows = await ctrl.MenusGet("order-test");
        var ids = rows.Select(r => r.Id).ToList();

        // 主键：sortOrder 升序（1 在 2 前）
        Assert.True(ids.IndexOf(FixedLate) < ids.IndexOf(FixedEarly), "sortOrder=1 must precede 2");
        // 次级键：响应中并列组子序列（保持响应序）== id 升序
        var respTied = ids.Where(FixedTied.Contains).ToList();
        Assert.Equal(FixedTied, respTied);
    }

    [Fact]
    [Trait("Fn", "M04.F04.I06")]
    public async Task Reorder_ordersBySortOrderThenId()
    {
        var (db, ctrl) = Make("client-menus-reorder");
        // Title 反排（tied-7 → tied-0 对应 id 升序）：修前 ThenBy(Title) 会吐 id 降序，必红
        db.SysMenus.Add(Menu(FixedEarly, "early", 2));
        db.SysMenus.Add(Menu(FixedLate, "late", 9));
        for (var i = 0; i < FixedTied.Length; i++)
        {
            db.SysMenus.Add(Menu(FixedTied[i], $"tied-{FixedTied.Length - 1 - i}", 5));
        }

        await db.SaveChangesAsync();
        var rows = await ctrl.Reorder("order-test", FixedLate.ToString(),
            new Saas.Identity.AspNetCore.Controllers.Generated.ReorderSysMenuRequest
            {
                OrderedMenuIds = new List<string> { FixedLate.ToString() },
            });
        var ids = rows.Select(r => r.Id).ToList();

        Assert.Equal(0, rows.First(r => r.Id == FixedLate).SortOrder); // indexOf 语义
        var respTied = ids.Where(FixedTied.Contains).ToList();
        Assert.Equal(FixedTied, respTied);
    }
}
