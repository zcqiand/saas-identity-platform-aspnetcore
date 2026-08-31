namespace Saas.Identity.AspNetCore.Domain.Entities;

// M01.F01 / M01.F02 PG native enum user_status — Npgsql.MapEnum<T>() 用 string-backed enum。
// 2026-08-31 contract-test M96.F02.I19：未注册时 Npgsql 把 "active" 当 text 发，PG 抛 42804。
public enum UserStatusPg
{
    active,
    invited,
    suspended,
    disabled,
}