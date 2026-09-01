namespace Saas.Identity.AspNetCore.Domain.Entities;

// M07.F01 PG native enum app_status — Npgsql.MapEnum<T>() 用 string-backed enum。
// 2026-09-01 contract-test M96.F02.I45：POST /admin/apps 写路径 42804
// （column "status" is of type app_status but expression is of type text）同款修法。
public enum AppStatusPg
{
    active,
    disabled,
}
