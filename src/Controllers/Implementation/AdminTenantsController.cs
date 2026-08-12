using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M00.F01 租户 CRUD（平台 admin）。
/// 继承 NSwag 生成的 AdminTenantsControllerBase 抽象类。
/// admin-level 端点不需 TenantGuard（JWT 校验已在 [Authorize] 层做）。
/// </summary>
public class AdminTenantsController : AdminTenantsControllerBase
{
    public override Task<Response2> TenantsGet(int? page, int? pageSize)
    {
        // M00.F01.I01 租户列表
        return Task.FromResult(new Response2
        {
            Items = InMemoryStore.Tenants,
            Page = page ?? 1,
            PageSize = pageSize ?? InMemoryStore.Tenants.Count,
            Total = InMemoryStore.Tenants.Count,
        });
    }

    public override Task<Tenant> TenantsPost(CreateTenantRequest body)
    {
        // M00.F01.I02 创建租户
        var t = new Tenant
        {
            Id = Guid.NewGuid(),
            Code = body.Code,
            Name = body.Name,
            Status = TenantStatus.Active,
            Settings = body.Settings,
        };
        InMemoryStore.Tenants.Add(t);
        return Task.FromResult(t);
    }

    public override Task<Tenant> TenantsGet(string id)
    {
        // M00.F01.I03 租户详情
        var gid = Guid.Parse(id);
        var t = InMemoryStore.Tenants.FirstOrDefault(x => x.Id == gid)
            ?? throw new KeyNotFoundException($"tenant {id} not found");
        return Task.FromResult(t);
    }

    public override Task<Tenant> TenantsPatch(string id, UpdateTenantRequest body)
    {
        // M00.F01.I04 更新租户
        var t = InMemoryStore.Tenants.First(x => x.Id == Guid.Parse(id));
        if (body.Name != null) t.Name = body.Name;
        t.Status = body.Status;
        return Task.FromResult(t);
    }

    public override Task TenantsDelete(string id)
    {
        // M00.F01.I05 删除租户
        InMemoryStore.Tenants.RemoveAll(x => x.Id == Guid.Parse(id));
        return Task.CompletedTask;
    }
}