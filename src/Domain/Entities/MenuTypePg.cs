namespace Saas.Identity.AspNetCore.Domain.Entities;

// M08.F01 PG native enum menu_type — Npgsql.MapEnum<T>() 用 string-backed enum。
// 2026-09-01 contract-test M96.F02.I51：menus 写路径配套（menu_type 列同页 V005）。
public enum MenuTypePg
{
    @group,
    page,
    action,
}
