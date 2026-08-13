using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
using DbApp = Saas.Identity.AspNetCore.Domain.Entities.App;
// alias 避免与 NSwag-generated DTO `App` 冲突
using ApiApp = Saas.Identity.AspNetCore.Controllers.Generated.App;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M07.F01 应用 CRUD（平台 admin）+ M07.F02.I06 启停用。
/// 平台级操作不需要 TenantGuard。
/// v0.4.0（M10.F04）：从 InMemoryStore 迁到 AppDbContext。
/// </summary>
public class AdminAppsController : AdminAppsControllerBase
{
    private readonly AppDbContext _db;

    public AdminAppsController(AppDbContext db) { _db = db; }

    // === DTO ↔ Entity 转换 ===

    private static ApiApp ToDto(DbApp e) => new()
    {
        Id = e.Id,
        Code = e.Code,
        Name = e.Name,
        Description = e.Description,
        Icon = e.Icon,
        SortOrder = e.SortOrder,
        Status = ToDtoStatus(e.Status),
        ClientId = e.ClientId,
        RedirectUris = e.RedirectUris,
        Scopes = e.Scopes,
        GrantTypes = e.GrantTypes.Select(g => ParseGrantType(g)).ToList(),
        IsFirstParty = e.IsFirstParty,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    private static OAuthGrantType ParseGrantType(string s) => s switch
    {
        "authorization_code" => OAuthGrantType.Authorization_code,
        "refresh_token" => OAuthGrantType.Refresh_token,
        "client_credentials" => OAuthGrantType.Client_credentials,
        "password" => OAuthGrantType.Password,
        _ => OAuthGrantType.Authorization_code,
    };

    private static AppStatus ToDtoStatus(string s) => s switch
    {
        "active" => AppStatus.Active,
        _ => AppStatus.Disabled,
    };

    private static string ToDbStatus(AppStatus s) => s switch
    {
        AppStatus.Active => "active",
        _ => "disabled",
    };

    // === endpoints ===

    public override async Task<Response> AppsGet(int? page, int? pageSize)
    {
        var p = page ?? 1;
        var ps = pageSize ?? 20;
        var items = await _db.Apps.OrderByDescending(a => a.CreatedAt)
            .Skip((p - 1) * ps).Take(ps).ToListAsync();
        var total = await _db.Apps.CountAsync();
        return new Response
        {
            Items = items.Select(ToDto).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<ApiApp> AppsPost(CreateAppRequest body)
    {
        var e = new DbApp
        {
            Id = Guid.NewGuid(),
            Code = body.Code,
            Name = body.Name,
            Description = body.Description,
            Icon = body.Icon,
            SortOrder = body.SortOrder,
            Status = ToDbStatus(body.Status),
            ClientId = body.ClientId,
            ClientSecretHash = body.ClientSecret != null ? $"plain:{body.ClientSecret}" : null,
            RedirectUris = body.RedirectUris?.ToList() ?? new(),
            Scopes = body.Scopes?.ToList() ?? new(),
            GrantTypes = body.GrantTypes?.Select(g => g.ToString().ToLowerInvariant()).ToList() ?? new(),
            IsFirstParty = body.IsFirstParty,
        };
        _db.Apps.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ApiApp> AppsGet(string appId)
    {
        var id = Guid.Parse(appId);
        var e = await _db.Apps.FirstAsync(a => a.Id == id);
        return ToDto(e);
    }

    public override async Task<ApiApp> AppsPatch(string appId, UpdateAppRequest body)
    {
        var id = Guid.Parse(appId);
        var e = await _db.Apps.FirstAsync(a => a.Id == id);
        if (body.Name != null) e.Name = body.Name;
        if (body.Description != null) e.Description = body.Description;
        if (body.Icon != null) e.Icon = body.Icon;
        e.SortOrder = body.SortOrder;
        e.Status = ToDbStatus(body.Status);
        if (body.RedirectUris != null) e.RedirectUris = body.RedirectUris.ToList();
        if (body.Scopes != null) e.Scopes = body.Scopes.ToList();
        if (body.GrantTypes != null) e.GrantTypes = body.GrantTypes.Select(g => g.ToString().ToLowerInvariant()).ToList();
        e.IsFirstParty = body.IsFirstParty;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task AppsDelete(string appId)
    {
        var id = Guid.Parse(appId);
        var e = await _db.Apps.FirstOrDefaultAsync(a => a.Id == id);
        if (e != null)
        {
            _db.Apps.Remove(e);
            await _db.SaveChangesAsync();
        }
    }

    public override async Task<ApiApp> Status(string appId, Body2 body)
    {
        var id = Guid.Parse(appId);
        var e = await _db.Apps.FirstAsync(a => a.Id == id);
        e.Status = ToDbStatus(body.Status);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }
}