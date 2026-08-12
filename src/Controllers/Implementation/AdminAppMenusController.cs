using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M08 菜单 CRUD（应用下）。
/// 平台级：admin 级 / 应用级菜单不需要 TenantGuard（菜单是 app 级，不是 tenant 级）。
/// </summary>
public class AdminAppMenusController : AdminAppMenusControllerBase
{
    public override Task<ICollection<Menu>> MenusGet(string appId)
    {
        // M08.F01.I01 菜单树
        var gid = Guid.Parse(appId);
        var menus = InMemoryStore.Menus.Where(m => m.AppId == gid).ToList();
        return Task.FromResult<ICollection<Menu>>(menus);
    }

    public override Task<Menu> MenusPost(string appId, CreateMenuRequest body)
    {
        // M08.F01.I02 创建菜单
        var m = new Menu
        {
            Id = Guid.NewGuid(),
            AppId = Guid.Parse(appId),
            Code = body.Code,
            Name = body.Name,
            Type = body.Type,
            Status = body.Status,
        };
        InMemoryStore.Menus.Add(m);
        return Task.FromResult(m);
    }

    public override Task<Menu> MenusGet(string appId, string menuId)
    {
        // M08.F01.I03 菜单详情
        return Task.FromResult(InMemoryStore.Menus.First(m => m.Id == Guid.Parse(menuId)));
    }

    public override Task<Menu> MenusPatch(string appId, string menuId, UpdateMenuRequest body)
    {
        // M08.F01.I04 更新菜单
        var m = InMemoryStore.Menus.First(x => x.Id == Guid.Parse(menuId));
        if (body.Name != null) m.Name = body.Name;
        m.Status = body.Status;
        return Task.FromResult(m);
    }

    public override Task MenusDelete(string appId, string menuId)
    {
        // M08.F01.I05 删除菜单
        InMemoryStore.Menus.RemoveAll(x => x.Id == Guid.Parse(menuId));
        return Task.CompletedTask;
    }

    public override Task<Menu> Parent(string appId, string menuId, Body body)
    {
        // M08.F02.I07 切换父级
        var m = InMemoryStore.Menus.First(x => x.Id == Guid.Parse(menuId));
        m.ParentId = string.IsNullOrEmpty(body.ParentId) ? Guid.Empty : Guid.Parse(body.ParentId);
        return Task.FromResult(m);
    }

    public override Task<ICollection<Menu>> Reorder(string appId, string menuId, ReorderMenuRequest body)
    {
        // M08.F02.I06 同级排序
        return Task.FromResult<ICollection<Menu>>(InMemoryStore.Menus.Where(m => m.AppId == Guid.Parse(appId)).ToList());
    }
}