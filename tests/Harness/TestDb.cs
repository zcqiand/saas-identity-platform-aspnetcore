using System;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Saas.Identity.AspNetCore.Domain.Entities;
using Saas.Identity.AspNetCore.Infrastructure.Persistence;

namespace Saas.Identity.AspNetCore.Tests.Harness;

/// <summary>
/// saas_test 真库测试基建。硬依赖共享 PG —— 连不上直接失败，不 skip：
/// InMemory provider 测不出 PG native enum / uuid[] / jsonb 的真实映射行为
/// （v0.2.26 lab-aspnetcore 教训：假数据层测试全绿、prod 首请求炸）。
///
/// 连接串读 SAAS_TEST_DATABASE_URL（Npgsql 格式），缺省回落共享 PG saas_test。
/// EF 不 Migrate（表结构由 shared SQL SSOT 管，saas_test 已就绪）。
/// dataSource builder 镜像 Program.cs（EnableDynamicJson + MapEnum 全套）——
/// 测试基建单独搭一条不同的 Npgsql 配置链 = 又一个组合根盲区。
/// </summary>
public static class TestDb
{
    public const string DefaultUrl =
        "Host=100.79.128.25;Port=5432;Database=saas_test;Username=postgres;Password=qiand68+++";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("SAAS_TEST_DATABASE_URL") ?? DefaultUrl;

    private static readonly NpgsqlDataSource DataSource = BuildDataSource();

    private static NpgsqlDataSource BuildDataSource()
    {
        var b = new NpgsqlDataSourceBuilder(ConnectionString);
        b.EnableDynamicJson();
        b.MapEnum<ApiKeyStatusPg>("api_key_status");
        b.MapEnum<AuditActionPg>("audit_action");
        b.MapEnum<UserStatusPg>("user_status");
        b.MapEnum<MembershipStatusPg>("membership_status");
        b.MapEnum<TenantStatusPg>("tenant_status");
        b.MapEnum<AppStatusPg>("app_status");
        b.MapEnum<MenuStatusPg>("menu_status");
        b.MapEnum<MenuTypePg>("menu_type");
        b.MapEnum<OAuthGrantTypePg>("oauth_grant_type");
        return b.Build();
    }

    public static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(DataSource)
            .Options;

        return new AppDbContext(options);
    }

    /// <summary>连接可用性硬断言：连不上即测试失败（不是 skip）。</summary>
    public static void RequireReachable()
    {
        using var ctx = CreateContext();
        ctx.Database.OpenConnection();
    }
}
