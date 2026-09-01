namespace Saas.Identity.AspNetCore.Domain.Entities;

// M00.F01 PG native enum tenant_status — Npgsql.MapEnum<T>() 用 string-backed enum。
// 2026-08-31 contract-test M96.F02.I30：未注册时 Npgsql 把 "active" 当 text 发，
// PG 抛 42804（column "status" is of type tenant_status but expression is of type text）。
// 与 UserStatusPg / ApiKeyStatusPg 同款修法（V001__init_tenants.sql 的三值枚举）。
public enum TenantStatusPg
{
    active,
    suspended,
    archived,
}
