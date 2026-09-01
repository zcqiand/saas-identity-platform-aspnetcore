namespace Saas.Identity.AspNetCore.Domain.Entities;

// M08.F01 PG native enum menu_status — Npgsql.MapEnum<T>() 用 string-backed enum。
// 2026-09-01 contract-test M96.F02.I51：POST /admin/apps/{id}/menus 写路径 42804
// （column "status" is of type menu_status but expression is of type text）同款修法。
public enum MenuStatusPg
{
    active,
    disabled,
}
