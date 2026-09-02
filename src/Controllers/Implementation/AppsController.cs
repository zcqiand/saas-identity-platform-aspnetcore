// /api/v1/apps/{code} - M04.F01 公共读侧：按 appCode 返回应用公开信息
//
// TypeSpec: getApp(@path code): AppPublicInfo
// 免鉴权（接入方侧边栏要显示应用名，不能强制管理员 JWT）。
// 2026-08-30 contract-test M96.F02.I06: 字段对齐 nextjs + msw
//   (id/code/name/description/icon/status, NOT OAuth 集成字段)

using Microsoft.EntityFrameworkCore;
using Saas.Identity.AspNetCore.Controllers.Generated;
using Saas.Identity.AspNetCore.Domain.Entities;
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
        // 2026-08-30 contract-test: 直接读 DB (status 是 PG native enum 的字符串表示 "active")
        var row = await _db.Apps
            .Where(a => a.Code == code && a.Status == AppStatusPg.active)
            .Select(a => new { a.Id, a.Code, a.Name, a.Description, a.Icon, a.Status })
            .FirstOrDefaultAsync();
        if (row is null)
        {
            // 真无记录时抛错; 走 GlobalExceptionHandler 转 404 ErrorResponse
            throw new KeyNotFoundException($"App '{code}' not found");
        }
        return new Saas.Identity.AspNetCore.Controllers.Generated.AppPublicInfo
        {
            Id = row.Id,
            Code = row.Code,
            Name = row.Name,
            Description = row.Description,
            Icon = row.Icon,
            Status = AppStatus.Active,
        };
    }
}