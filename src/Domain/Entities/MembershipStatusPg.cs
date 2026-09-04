// M09 membership_status PG native enum（active/invited/removed，V003 memberships 表）。
// 与 UserStatusPg / TenantStatusPg 同款 string-backed enum：
// Npgsql.MapEnum + AppDbContext HasConversion（DbMembership.Status 是 string）。
// 未注册/未转换时 Npgsql 把 "active" 当 text 发，PG 抛 42804 —— 真库测试
// （MeMenusPgTest）首跑抓出；InMemory provider 永远测不出。
public enum MembershipStatusPg
{
    active,
    invited,
    removed,
}
