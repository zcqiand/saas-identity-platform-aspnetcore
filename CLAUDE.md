# saas-identity-platform-aspnetcore

> ASP.NET Core 8 + xUnit + JwtBearer。**v0.2.0 NSwag codegen**，读 shared/openapi.yaml → 本仓 Controllers。

## 1. 这是什么

saas-identity-platform 的 C# 后端（已落地 v0.2.0 迁移）。

- **NSwag**：本仓 `dotnet tool install --global NSwag.ConsoleCore` + `aspnetcore.nswag` 配置 + `scripts/gen-shared.sh`（先 cd ../shared && npm run emit:openapi，再 nswag run）
- **Controllers**：NSwag 读 shared/openapi.yaml → `src/Controllers/Generated/Controllers.cs`（11 abstract base + 33 DTO record）
- **业务实现**：手写 partial classes `src/Controllers/Implementation/<Tag>Controller.cs`（继承 NSwag 抽象基类，覆盖 abstract 方法 + 调 TenantGuard）
- **运行时存储**：`src/Controllers/Implementation/InMemoryStore.cs`（scaffold fixture，进程重启丢失）
- **认证**：JwtBearer + tenant_id claim 校验（`src/Security/TenantGuard.cs`，每个 tenant-scoped endpoint 第一行调）

## 2. 禁止事项（v0.2.0 hard rules）

- ❌ 禁止手写 Controller 路由（路由必须由 NSwag 生成）
- ❌ 禁止在 Controller 方法体内写业务逻辑前忘记调 TenantGuard.VerifyPathTenant(tenantId)
- ❌ 禁止手写 fetch / HttpClient 调用（直接实现 abstract 方法即可，框架绑定）
- ❌ 禁止把 `@saas/identity-platform-shared` 列为依赖（NSwag 直接读相对路径）
- ❌ 禁止删手写 `src/Controllers/Implementation/` 目录下的 concrete 类（NSwag 生成的是 abstract base）
- ❌ 禁止直接编辑 `src/Controllers/Generated/Controllers.cs`（NSwag 产物，下次 gen-shared 重写）
- ❌ 禁止修改 InMemoryStore 里的字段名（NSwag 生成的 DTO 与之强绑定）
- ❌ 禁止测试并行运行（`[assembly: CollectionBehavior(DisableTestParallelization = true)]`）—— InMemoryStore 是 static fixture，并发修改会 throw `Collection was modified`

## 3. 4 个核心基建文件（其他 5 仓镜像迁移时要复制这 4 个）

| 文件 | 职责 |
| --- | --- |
| `src/Security/TenantContext.cs` | 从 `IHttpContextAccessor` 读 JWT tenant_id claim |
| `src/Security/TenantGuard.cs` | path tenantId vs JWT claim 校验，throw UnauthorizedAccessException on mismatch |
| `src/Controllers/Implementation/InMemoryStore.cs` | 进程内 fixture 数据（Tenants/Users/Roles/Apps/Menus/ApiKeys/AuditEvents + Reset() 给测试用）|
| `scripts/gen-shared.sh` | NSwag 集成：emit OpenAPI.yaml → 产 Controllers.cs |

## 4. 指向别处

- shared 仓：`../saas-identity-platform-shared`（**只读 `generated/openapi/openapi.yaml`**）
- 迁移指南：react 仓 `docs/saas-identity-platform-v0.2.0-migration.md`（vue/nextjs/aspnetcore 必读 §3.5「为什么 TS/Java/CS 不再放 shared」+ §5 react 仓改动清单）
- function-tree：`docs/functions/function-tree.md`

## 5. 工作循环

1. 改 shared TypeSpec？→ `cd ../saas-identity-platform-shared && npm run emit:openapi`
2. 重新生成 Controllers？→ `bash scripts/gen-shared.sh`
3. 改 concrete Controller 业务逻辑？→ `src/Controllers/Implementation/<Tag>Controller.cs`
4. `dotnet test` 验证
5. `python scripts/gate.py -p saas-identity-platform-aspnetcore`