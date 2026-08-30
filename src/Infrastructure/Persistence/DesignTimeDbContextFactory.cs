using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence;

/// <summary>
/// Design-time DbContext factory for `dotnet ef migrations add` 命令。
/// 运行命令时不会启动 ASP.NET host，必须从这里直接构造 DbContext。
/// 连接字符串从环境变量 PG_CONNECTION 读取；缺失则 fail-fast
/// （CLAUDE.md「禁止 env 默认值兜底」硬规则），不让 prod 密码留进仓里。
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("PG_CONNECTION");
        if (string.IsNullOrEmpty(conn))
        {
            throw new InvalidOperationException(
                "PG_CONNECTION 未配置。dev 加载 .env.test；prod 由 deploy 脚本注入。" +
                "本规则遵循 CLAUDE.md「禁止 env 默认值兜底」：secret 缺失必须 fail-fast。");
        }

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn, npg =>
            {
                npg.MigrationsHistoryTable("__ef_migrations_history");
                npg.MigrationsAssembly("Saas.Identity.AspNetCore");
            })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(opts);
    }
}