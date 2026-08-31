namespace Saas.Identity.AspNetCore.Domain.Entities;

// M06.F03 PG native enum audit_action — Npgsql.MapEnum<T>() 用 string-backed enum。
public enum AuditActionPg
{
    user_created,
    user_updated,
    user_deleted,
    role_assigned,
    role_revoked,
    login_success,
    login_failed,
    oauth_token_issued,
    api_key_created,
    api_key_revoked,
}
