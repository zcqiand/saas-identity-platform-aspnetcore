namespace Saas.Identity.AspNetCore.Domain.Entities;

// M05.F01 PG native enum api_key_status — Npgsql.MapEnum<T>() 用 string-backed enum。
public enum ApiKeyStatusPg
{
    active,
    revoked,
    expired,
}
