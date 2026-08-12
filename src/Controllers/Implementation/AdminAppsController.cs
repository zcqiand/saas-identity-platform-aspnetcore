using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M08.F01 应用 CRUD（平台 admin）。M04 的一部分。
/// 平台级操作不需要 TenantGuard。
/// </summary>
public class AdminAppsController : AdminAppsControllerBase
{
    public override Task<Response> AppsGet(int? page, int? pageSize)
    {
        // M08.F01 / M04.F01.I01 应用列表
        return Task.FromResult(new Response
        {
            Items = InMemoryStore.Apps,
            Page = page ?? 1,
            PageSize = pageSize ?? InMemoryStore.Apps.Count,
            Total = InMemoryStore.Apps.Count,
        });
    }

    public override Task<App> AppsPost(CreateAppRequest body)
    {
        // M04.F01.I02 创建应用
        var a = new App { Id = Guid.NewGuid(), Code = body.Code, Name = body.Name, Status = body.Status };
        InMemoryStore.Apps.Add(a);
        return Task.FromResult(a);
    }

    public override Task<App> AppsGet(string appId)
    {
        // M04.F01.I03 应用详情
        return Task.FromResult(InMemoryStore.Apps.First(x => x.Id == Guid.Parse(appId)));
    }

    public override Task<App> AppsPatch(string appId, UpdateAppRequest body)
    {
        // M04.F01.I04 更新应用
        var a = InMemoryStore.Apps.First(x => x.Id == Guid.Parse(appId));
        if (body.Name != null) a.Name = body.Name;
        a.Status = body.Status;
        return Task.FromResult(a);
    }

    public override Task AppsDelete(string appId)
    {
        // M04.F01.I05 删除应用
        InMemoryStore.Apps.RemoveAll(x => x.Id == Guid.Parse(appId));
        return Task.CompletedTask;
    }

    public override Task<App> Status(string appId, Body2 body)
    {
        // M04.F02.I06 启用/停用应用
        var a = InMemoryStore.Apps.First(x => x.Id == Guid.Parse(appId));
        a.Status = body.Status;
        return Task.FromResult(a);
    }
}