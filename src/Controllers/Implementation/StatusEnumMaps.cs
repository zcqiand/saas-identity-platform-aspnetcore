using Saas.Identity.AspNetCore.Controllers.Generated;

namespace Saas.Identity.AspNetCore.Controllers.Implementation;

/// <summary>
/// DB smallint ↔ API 字符串枚举映射（公共静态类，2026-09-12 抽取自 TenantMembersController）。
///
/// 背景：9/7 重组后所有 status 列从 PG native enum 改 smallint（Program.cs 注释），
/// 而 NSwag 生成的 C# 枚举从 0 起编号（Active=0/Invited=1/...）。
/// DB 值域（全家族约定，ADR-0032 起 4 值）：
///   0=disabled / 1=active / 2=invited / 3=suspended。
/// 裸 cast (Enum)dbValue 全部 off-by-one：DB 1(active) 落到生成枚举 Invited。
/// 所有 DB smallint ↔ 枚举转换必须走这里，禁止裸 cast。
/// </summary>
public static class StatusEnumMaps
{
    // sys_user.status：DB 1=active, 2=invited, 0=disabled。
    // 契约层 SysUserStatus 只有 3 值（active/invited/disabled，无 suspended —— suspended
    // 是 tenant_member 语义）；DB 3 理论不该出现在 sys_user，兜底 Disabled。
    public static SysUserStatus MapUserStatus(short db) => db switch
    {
        1 => SysUserStatus.Active,
        2 => SysUserStatus.Invited,
        _ => SysUserStatus.Disabled,
    };

    // tenant_member.status：DB 0=disabled / 1=active / 2=invited / 3=suspended
    //（ADR-0032：TenantMemberStatus 扩为 4 值，全家族 msw/nextjs/springboot 同码表）。
    public static TenantMemberStatus MapMemberStatus(short db) => db switch
    {
        1 => TenantMemberStatus.Active,
        2 => TenantMemberStatus.Invited,
        3 => TenantMemberStatus.Suspended,
        _ => TenantMemberStatus.Disabled,
    };

    // MapMemberStatus 反向（status 端点写入用，避免把 Active 写成 DB 0=disabled）
    public static short ToDbMemberStatus(TenantMemberStatus s) => s switch
    {
        TenantMemberStatus.Active => (short)1,
        TenantMemberStatus.Invited => (short)2,
        TenantMemberStatus.Suspended => (short)3,
        _ => (short)0,
    };

    // tenant.status：DB 1=active, 2=suspended（生成枚举值域 active/suspended，见 openapi.yaml）
    public static TenantStatus MapTenantStatus(short db)
        => db == 1 ? TenantStatus.Active : TenantStatus.Suspended;

    // MapTenantStatus 反向（PATCH 写入用）
    public static short ToDbTenantStatus(TenantStatus s)
        => s == TenantStatus.Active ? (short)1 : (short)2;
}
