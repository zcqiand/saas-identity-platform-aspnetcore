using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using DbClient = Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated.OauthClient;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;
// alias 避免与 NSwag-generated DTO `OAuthClient` 冲突
using ApiClient = Saas.Identity.AspNetCore.Controllers.Generated.OAuthClient;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// Concrete M04.F01 OAuth client CRUD + M04.F02.I01 status 切换（平台 admin，无 tenant scope）。
/// 2026-09-10 审计 critical 补齐：Generated 基类早已生成但缺 concrete → DI 500。
/// clientId 比对语义：字符串列 oauth_clients.client_id（与 public ClientsController 一致，非 UUID）。
/// ClientSecret 只入库不回传（DTO 无该字段——M04.F01.I03 密钥仅回指纹约定的最小实现）。
/// </summary>
public class AdminClientsController : AdminClientsControllerBase
{
    private readonly AppDbContext _db;

    public AdminClientsController(AppDbContext db) { _db = db; }

    private static ApiClient ToDto(DbClient e) => new()
    {
        Id = e.Id,
        ClientId = e.ClientId,
        ClientName = e.ClientName,
        GrantTypes = e.GrantTypes,
        RedirectUris = e.RedirectUris,
        Scopes = e.Scopes,
        AccessTokenValidity = e.AccessTokenValidity,
        RefreshTokenValidity = e.RefreshTokenValidity,
        AutoApprove = e.AutoApprove,
        Status = e.Status,
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc)),
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(e.UpdatedAt, DateTimeKind.Utc)),
    };

    public override async Task<Response> ClientsGet(int? page, int? pageSize)
    {
        var p = page ?? 0;
        var ps = pageSize ?? 20;
        var items = await _db.OauthClients.OrderByDescending(c => c.CreatedAt)
            .Skip(p * ps).Take(ps).ToListAsync();
        var total = await _db.OauthClients.CountAsync();
        return new Response
        {
            Items = items.Select(ToDto).ToList(),
            Page = p,
            PageSize = ps,
            Total = total,
        };
    }

    public override async Task<ApiClient> ClientsPost(CreateOAuthClientRequest body)
    {
        var e = new DbClient
        {
            Id = Guid.NewGuid(),
            ClientId = body.ClientId,
            ClientSecret = body.ClientSecret,
            ClientName = body.ClientName,
            GrantTypes = body.GrantTypes,
            RedirectUris = body.RedirectUris,
            Scopes = body.Scopes,
            AccessTokenValidity = body.AccessTokenValidity,
            RefreshTokenValidity = body.RefreshTokenValidity,
            AutoApprove = body.AutoApprove,
            Status = 1, // active
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.OauthClients.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task<ApiClient> ClientsGet(string clientId)
    {
        var e = await _db.OauthClients.FirstOrDefaultAsync(c => c.ClientId == clientId)
            ?? throw new KeyNotFoundException($"client {clientId} not found");
        return ToDto(e);
    }

    public override async Task<ApiClient> ClientsPatch(string clientId, UpdateOAuthClientRequest body)
    {
        var e = await _db.OauthClients.FirstOrDefaultAsync(c => c.ClientId == clientId)
            ?? throw new KeyNotFoundException($"client {clientId} not found");
        if (body.ClientName != null) e.ClientName = body.ClientName;
        if (body.GrantTypes != null) e.GrantTypes = body.GrantTypes;
        if (body.RedirectUris != null) e.RedirectUris = body.RedirectUris;
        if (body.Scopes != null) e.Scopes = body.Scopes;
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public override async Task ClientsDelete(string clientId)
    {
        var e = await _db.OauthClients.FirstOrDefaultAsync(c => c.ClientId == clientId)
            ?? throw new KeyNotFoundException($"client {clientId} not found");
        _db.OauthClients.Remove(e);
        await _db.SaveChangesAsync();
        Response.StatusCode = StatusCodes.Status204NoContent;
    }

    public override async Task<ApiClient> Status(string clientId, Body body)
    {
        var e = await _db.OauthClients.FirstOrDefaultAsync(c => c.ClientId == clientId)
            ?? throw new KeyNotFoundException($"client {clientId} not found");
        e.Status = (short)body.Status;
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }
}
