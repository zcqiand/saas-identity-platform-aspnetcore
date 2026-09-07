// /api/v1/clients/{clientId} - M04 公共读侧：按 oauth client_id 返回应用公开信息。
//
// v0.5.0 NSwag 重 emit 后：AppPublicInfo → OAuthClientPublicInfo；Apps → Clients（路由
// /apps/{code} → /clients/{clientId}）；param name code → clientId。

using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

public class ClientsController : ClientsControllerBase
{
    private readonly AppDbContext _db;

    public ClientsController(AppDbContext db)
    {
        _db = db;
    }

    public override async Task<OAuthClientPublicInfo> Clients(string clientId)
    {
        var row = await _db.OauthClients
            .Where(a => a.ClientId == clientId && a.Status == 1)
            .FirstOrDefaultAsync();
        if (row is null)
        {
            throw new KeyNotFoundException($"Client '{clientId}' not found");
        }
        return new OAuthClientPublicInfo
        {
            // v0.5.0 NSwag 重 emit：OAuthClientPublicInfo 形状变 { clientId, clientName, status }；
            // Id / Name 字段取消（clientId 是 OAuth 标准命名）。
            ClientId = row.ClientId,
            ClientName = row.ClientName,
            Status = (int)row.Status,
        };
    }
}