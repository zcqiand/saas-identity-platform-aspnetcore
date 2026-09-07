// /api/v1/apps/{clientId} - M04.F01 公共读侧：按 oauth client_id 返回应用公开信息。
//
// 9/7 重组：oauth_client 表已 rename `code` → `client_id`（OAuth 2.0 RFC 6749 标准命名），
// 原 `name` → `client_name`。DTO AppPublicInfo 字段名 Code/Name 保留为业务概念（public
// API 字段名不变以兼容 contract-test），entity 侧做映射。

using Saas.Identity.AspNetCore.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

public class AppsController : AppsControllerBase
{
    private readonly AppDbContext _db;

    public AppsController(AppDbContext db)
    {
        _db = db;
    }

    public override async Task<Saas.Identity.AspNetCore.Controllers.Generated.AppPublicInfo> Apps(string code)
    {
        // 按 client_id 查（scaffold 已重命名 code → client_id）
        var row = await _db.OauthClients
            .Where(a => a.ClientId == code && a.Status == 1)
            .FirstOrDefaultAsync();
        if (row is null)
        {
            throw new KeyNotFoundException($"App '{code}' not found");
        }
        // DTO 字段映射：ClientId → Code, ClientName → Name（DTO 字段名保留）
        return new Saas.Identity.AspNetCore.Controllers.Generated.AppPublicInfo
        {
            Id = row.Id,
            Code = row.ClientId,
            Name = row.ClientName,
            Description = null, // OauthClient 表无 description 列
            Icon = null,        // OauthClient 表无 icon 列
            Status = AppStatus.Active,
        };
    }
}