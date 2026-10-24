# SaaS 多租户多应用身份平台 · ASP.NET Core 后端

SaaS 身份平台的 C# 后端 —— NSwag codegen abstract Controllers + 手写 partial 实现（属于 saas-identity 多栈家族）。

本仓为《（书稿信息待补）》案例（待补）的可运行配套工程，是书稿代码块的 **source of truth**。

## 快速开始

```bash
dotnet restore
dotnet test                        # 全量测试（xUnit，串行）
bash scripts/gen-shared.sh         # 改了 shared 仓后同步 NSwag 产物
dotnet run --project src           # 本地起服务
```

## 功能特性

- NSwag 读 shared OpenAPI 生成 11 个 abstract base Controller + 33 个 DTO record
- 手写 partial 类 `src/Controllers/Implementation/` 承接业务；InMemoryStore 运行时存储（重启丢失）
- JwtBearer + tenant_id claim 校验（TenantGuard，每个 tenant-scoped endpoint 第一行调）

## 技术栈

| 技术 | 版本 |
| :--- | :--- |
| .NET | 8.0 (net8.0) |
| JwtBearer | 8.0.0 |
| EF Core | 8.0.0 |
| Swashbuckle.AspNetCore | 6.6.2 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.0 |
| xUnit | 2.9.0 |
| Moq | 4.20.70 |

> 依赖版本与 `version-lock.json` 的 `version_lock` 一致，不引入 lock 外的库。

## 配套书籍及章节映射

| 章 | 主题 | 对应源文件 |
| :--- | :--- | :--- |
| （待补） | | |

### 书一《ASP.NET Core 从入门到项目实践》

- 书稿定位：xr-know-016，第 26、27、28、29、30、31 章正文代码块挂本仓 `source=` 锚点
- 配套版本：`v0.3.33-20260928`（registry 冻结 tag，代码以此为准）

| 章 | 章题 | 对应源文件 |
| :--- | :--- | :--- |
| 26 | Identity 用户体系 | `src/Controllers/Implementation/AdminTenantsController.cs` + `src/Controllers/Implementation/TenantMembersController.cs` + `src/Controllers/Implementation/TenantRolesController.cs` + `src/Controllers/Implementation/TenantRoleMenusController.cs` + `src/Controllers/Implementation/MeController.cs` + `src/Controllers/Implementation/TenantApplicationsController.cs` |
| 27 | JWT 与 API 鉴权 | `src/Controllers/Implementation/AuthApi.cs` |
| 28 | OAuth/OIDC 与外部登录 | `src/Controllers/Implementation/AdminClientsController.cs` + `src/Controllers/Implementation/ClientsController.cs` + `src/Controllers/Implementation/OAuthController.cs` |
| 29 | 数据保护与安全加固 | `src/Security/FailedLoginStore.cs` + `src/Security/TenantGuard.cs` |
| 30 | 单元测试：xUnit 与 Moq | `src/Security/FailedLoginStore.cs` |
| 31 | 集成测试与 Testcontainers | `tests/ErrorSemanticsTests.cs` + `tests/ModelStateValidationTests.cs` |
## 快速链接

- [CLAUDE.md](CLAUDE.md) — 开发约定与编码规范
- [系统架构.md](docs/ARCHITECTURE.md) — 结构 / 边界 / 数据流 / 决策
- [功能规格.md](docs/functions/function-tree.md) — 功能名称、描述与验收标准
- [未来开发计划](PLAN.md) — 待办与迭代方向
- [更新日志](CHANGELOG.md) — 版本变更记录
