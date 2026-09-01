namespace Saas.Identity.AspNetCore.Domain.Entities;

// M07.F01 PG native enum oauth_grant_type — Npgsql.MapEnum<T>() 用 string-backed enum。
// 2026-09-01 contract-test M96.F02.I45：POST /admin/apps 写 grant_types（enum 数组）
// 42804（column "grant_types" is of type oauth_grant_type[] but expression is of
// type text[]）——List<string> 参数化成 text[]，需 enum 数组映射。
public enum OAuthGrantTypePg
{
    authorization_code,
    refresh_token,
    client_credentials,
}
