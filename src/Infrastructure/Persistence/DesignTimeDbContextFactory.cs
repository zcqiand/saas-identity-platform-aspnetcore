using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Saas.Identity.AspNetCore.Infrastructure.Persistence;

/// <summary>
/// Design-time DbContext factory for `dotnet ef migrations add` 命令。
/// 运行命令时不会启动 ASP.NET host，必须从这里直接构造 DbContext。
/// 连接字符串从环境变量 PG_CONNECTION 读取，fallback 到本地默认。
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("PG_CONNECTION")
            ?? "Host=100.79.128.25;Port=5432;Database=saas_dev;Username=postgres;Password=qiand68+++";

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