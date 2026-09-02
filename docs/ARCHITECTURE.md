# saas-identity-platform-aspnetcore Architecture

> 本仓是 `saas-identity-platform` 产品家族的 ASP.NET Core 8 后端（v0.2.0 已落地 NSwag codegen）。
> 文档只描述 *架构*（结构 / 边界 / 数据流 / 决策）；编码细则见 `CLAUDE.md`，单点决策见 `docs/adr/`。

---

## 0. 阅读路径

| 你是… | 直接看 |
|---|---|
| 新人，30 分钟搞懂本仓 | §1 → §2 → §3.1（NSwag codegen 链） |
| 想加一个新接口 | §3.1 → §3.2 → §5（与契约仓同步） |
| 想加一个 tenant-scoped 端点 | §3.4（TenantGuard）→ §4（流程） |
| 跨端调试不通 | §3.5（OAuth 2.0 / JWT / CORS）→ [父仓 §6](D:/qiand-life/1-projects/xr-code-suite/docs/ARCHITECTURE.md) |
| 想问「为什么这样设计」 | §7（决策索引）→ 对应 ADR |

---

## 1. 角色与定位

`saas-identity-platform-aspnetcore` 是 **saas-identity-platform 家族的 7 个子仓之一**（家族角色见父仓 [ARCHITECTURE.md §2.2](../../docs/ARCHITECTURE.md)），承担「**后端 2/2**」的角色：

| 维度 | 内容 |
|---|---|
| **技术栈** | ASP.NET Core 8 (`net8.0`) + xUnit + JwtBearer + EF Core 8 / Npgsql + NSwag |
| **运行时** | Kestrel，dev 默认 :5000，prod 由 deploy 脚本分配 |
| **C# / .NET 版本** | `<TargetFramework>net8.0</TargetFramework>`（`src/Saas.Identity.AspNetCore.csproj:4`） |
| **路由 / DTO 来源** | NSwag 读 `../saas-identity-platform-shared/generated/openapi/openapi.yaml` → `src/Controllers/Generated/Controllers.cs`（11 个 abstract base + DTO records） |
| **业务实现** | 手写 `partial class` `src/Controllers/Implementation/<Tag>Controller.cs`，继承 NSwag 抽象基类，覆盖 abstract 方法 + 调 `TenantGuard.VerifyPathTenant(...)` |
| **运行时存储** | EF Core 8 + Npgsql 读 shared PG（schema 由 saas-identity-platform-shared V001..V016 SQL 单一来源管理，本仓 `Program.cs` 启动不调 `Database.Migrate()`） |
| **认证 (OAuth 2.0 + JWT HS256)** | `JwtIssuer.IssueAccessToken` 签 HS256（`Security/JwtIssuer.cs`）；`AddJwtBearer()` + `TokenValidationParameters` 真验签；prod 走 `Jwt:SigningKey` 对称密钥或 JWKS |
| **共享 JWT key** | 与 saas-nextjs / saas-springboot 同一 `Jwt:SigningKey`，HS256 互签 |

**绝不**：

- 写业务路由（路由必须 NSwag 生成）；
- 在 Controller 方法体里写业务逻辑前忘记调 `TenantGuard.VerifyPathTenant(tenantId)`；
- 手写 `HttpClient` / `fetch` 调用（直接实现 abstract 方法即可，框架绑定）；
- 把 `@saas/identity-platform-shared` 列为 NuGet / 项目依赖（NSwag 直接读相对路径 `../shared/...`）；
- 直接编辑 `src/Controllers/Generated/Controllers.cs`（NSwag 产物，下次 `gen-shared.sh` 重写）；
- 测试并行运行（历史原因：InMemoryStore 是 static fixture；2026-08-30 已删 InMemoryStore 并落到 AppDbContext 的 InMemory provider，本规则随之失效，留此条防止误恢复 InMemoryStore 时漏掉并行约束）。

---

## 2. 目录骨架

```
saas-identity-platform-aspnetcore/
├── CLAUDE.md                              ← 入口：v0.2.0 hard rules + 4 个核心基建文件
├── .harness/stack.json                    ← suite 门禁读的项目自描述
├── docs/
│   ├── ARCHITECTURE.md                    ← 本文件
│   ├── functions/function-tree.md         ← F/I 级功能清单（M00-M09）
│   ├── design/                            ← 流程/设计（人评审）
│   └── conventions/                       ← 本仓编码细则
├── scripts/
│   ├── gen-shared.sh                      ← emit OpenAPI.yaml + nswag run + cp SQL
│   ├── gen-trace.sh / gen-trace.py        ← trace_cmd（产 .state/trace.json）
│   └── check-ef-mirrors-sql.sh            ← EF ↔ shared SQL diff 钩子（ADR-0010）
├── src/
│   ├── Saas.Identity.AspNetCore.csproj    ← net8.0 + JwtBearer + Swashbuckle + EF8 + Npgsql
│   ├── Program.cs                         ← DI 注册 + JwtBearer + CORS + Swagger + health
│   ├── Controllers/
│   │   ├── Generated/Controllers.cs       ← NSwag 产物（gitignored）
│   │   └── Implementation/                ← 手写 partial class（11 个 controller）
│   │       └── (InMemoryStore.cs 已删除 2026-08-30；运行时全部走 AppDbContext)
│   │       ├── AuthController.cs
│   │       ├── OauthController.cs
│   │       ├── MeController.cs
│   │       ├── AdminTenantsController.cs
│   │       ├── AdminAppsController.cs
│   │       ├── AdminAppMenusController.cs
│   │       ├── TenantUsersController.cs
│   │       ├── TenantRolesController.cs
│   │       ├── TenantRoleMenusController.cs
│   │       ├── TenantApiKeysController.cs
│   │       └── TenantAuditController.cs
│   ├── Security/
│   │   ├── TenantContext.cs               ← IHttpContextAccessor 读 JWT tenant_id claim
│   │   ├── TenantGuard.cs                 ← 路径 tenantId vs JWT claim 校验
│   │   └── JwtIssuer.cs                   ← HS256 签 access token（OAuth 共用）
│   ├── Domain/Entities/                   ← EF Core entities（10 个 entity）
│   ├── Infrastructure/Persistence/
│   │   ├── AppDbContext.cs                ← EF Core DbContext（ADR-0010）
│   │   └── DesignTimeDbContextFactory.cs  ← dotnet ef 工具链工厂
│   ├── bin/  obj/                         ← 构建产物（gitignored）
│   └── appsettings*.json                  ← 由 .csproj 显式 Include + CopyToOutput
├── tests/
│   ├── Saas.Identity.AspNetCore.Tests.csproj
│   ├── (TestBase.cs / SequentialCollection.cs 已删除 2026-08-30；并发约束随之解绑) / StubTenantContext.cs
│   ├── Harness/FnAttribute.cs              ← xUnit Fact/Theory wrapper
│   ├── TenantGuardTests.cs
│   └── Controllers/                       ← 11 个 controller 各一份测试
├── global.json                            ← .NET SDK pin
├── appsettings.json / appsettings.Development.json
├── aspnetcore.nswag                       ← NSwag 配置（读 ../shared/openapi.yaml）
├── Dockerfile                             ← multi-stage build
└── .csproj / .csproj                      ← src + tests 两个
```

### 关键路径速查

| 文件 | 职责 |
|---|---|
| `src/Security/TenantContext.cs` | 从 `IHttpContextAccessor` 读 JWT `tenant_id` claim（virtual 便于测试 stub） |
| `src/Security/TenantGuard.cs` | path `tenantId` vs JWT claim 校验；不匹配 throw `UnauthorizedAccessException` |
| `src/Security/JwtIssuer.cs` | HS256 access token 签发（AuthController + OauthController 共用） |
| `src/Controllers/Implementation/InMemoryStore.cs` | **已删除 2026-08-30** —— 运行时全走 `AppDbContext` |
| `scripts/gen-shared.sh` | NSwag 集成：`(cd ../shared && npm run emit:openapi)` → `nswag run aspnetcore.nswag` → cp SQL |
| `aspnetcore.nswag` | NSwag 配置（`controllerStyle: Abstract` + `typeStyle: Record` + `output: src/Controllers/Generated/Controllers.cs`） |
| `src/Program.cs` | DI 注册 + JwtBearer 双配置（dev/prod）+ CORS + Swagger + health endpoint |
| `tests/SequentialCollection.cs` | **已删除 2026-08-30** —— InMemoryStore 删了，并发约束随之解绑 |

---

## 3. 核心模块

### 3.1 NSwag codegen 链

**入口**：`scripts/gen-shared.sh`（bash 三步）。完整脚本见 [gen-shared.sh](../../saas-identity-platform-aspnetcore/scripts/gen-shared.sh)。

```
[gen-shared] step 1/2 — shared: emit OpenAPI.yaml...
(cd ../saas-identity-platform-shared && npm run emit:openapi)

[gen-shared] step 2/3 — aspnetcore: NSwag → src/Controllers/Generated/ + src/Models/Generated/...
mkdir -p src/Controllers/Generated src/Models/Generated
nswag run aspnetcore.nswag

[gen-shared] step 3/3 — DB: copy shared/sql/migrations/* + 触发 EF migrations script...
cp ../shared/sql/migrations/V*.sql ./Migrations/
```

**NSwag 配置**（`aspnetcore.nswag` 关键开关）：

| 字段 | 值 | 作用 |
|---|---|---|
| `runtime` | `Net80` | .NET 8 运行时 |
| `documentGenerator.fromDocument.url` | `../saas-identity-platform-shared/generated/openapi/openapi.yaml` | 读 shared 产物（**绝不**走 npm 依赖） |
| `codeGenerators.openApiToCSharpController.operationGenerationMode` | `MultipleClientsFromFirstTagAndPathSegments` | 按 OpenAPI tag + path 段拆 controller |
| `controllerStyle` | `Abstract` | 产物是 abstract base，**不**生成具体路由 |
| `controllerTarget` | `AspNetCore` | 路由用 ASP.NET Core 约定 |
| `output` | `src/Controllers/Generated/Controllers.cs` | 单文件输出 |
| `typeStyle` | `Record` | DTO 用 record（C# 9+） |
| `jsonLibrary` | `SystemTextJson` | 走 STJ，不引 Newtonsoft |
| `generateDtoTypes` | `true` | DTO 与 controller 同文件生成 |
| `addNullableAnnotations` | `true` | `#nullable enable` 配套 |

**产物形态**：单文件 `src/Controllers/Generated/Controllers.cs` 含 11 个 abstract base（每个 Tag 一个）+ 所有 DTO records。**手写业务逻辑不直接碰这个文件**，而是写 `src/Controllers/Implementation/<Tag>Controller.cs` 作为 `partial class` 继承之。

**DB 镜像（ADR-0010 主线，详见 [§6](#6-adr-0010-ef-core-migrations-待落地)）**：`gen-shared.sh` step 3 cp shared SQL 到 `Migrations/`，再 `scripts/check-ef-mirrors-sql.sh` diff EF 输出 ↔ shared SQL。

### 3.2 手写 partial Controllers

每个 OpenAPI tag 对应一个手写 controller 在 `src/Controllers/Implementation/`：

| 手写 controller | 继承自（NSwag 产） | 业务模块 |
|---|---|---|
| `AuthController.cs` | `AuthControllerBase` | M03 密码登录（`JwtIssuer.IssueAccessToken`） |
| `OauthController.cs` | `OauthControllerBase` | M03/M04 OAuth 授权码 + token 交换 + refresh |
| `MeController.cs` | `MeControllerBase` | M00 whoami / 列出我的租户成员关系 / 切租户 |
| `AdminTenantsController.cs` | `AdminTenantsControllerBase` | M00 平台级租户 CRUD |
| `AdminAppsController.cs` | `AdminAppsControllerBase` | M04 平台级 App CRUD + 启用停用 |
| `AdminAppMenusController.cs` | `AdminAppMenusControllerBase` | M08 菜单 CRUD + 结构维护 |
| `TenantUsersController.cs` | `TenantUsersControllerBase` | M01 用户 CRUD + 角色分配 |
| `TenantRolesController.cs` | `TenantRolesControllerBase` | M02 角色 CRUD + 权限绑定 |
| `TenantRoleMenusController.cs` | `TenantRoleMenusControllerBase` | M09 角色菜单授权 |
| `TenantApiKeysController.cs` | `TenantApiKeysControllerBase` | M05 API Key 生命周期 |
| `TenantAuditController.cs` | `TenantAuditControllerBase` | M06 审计事件查询 + 留存策略 |

**形态约定**：

```csharp
// 例（TenantUsersController.cs 风格，非真实代码）
public class TenantUsersController : TenantUsersControllerBase
{
    private readonly TenantGuard _guard;
    private readonly InMemoryStore _store; // 2026-08-30 后改为 AppDbContext _db

    public TenantUsersController(TenantGuard guard)
    {
        _guard = guard;
    }

    public override Task<IActionResult> ListUsersAsync(string tenantId, ...)
    {
        _guard.VerifyPathTenant(tenantId);   // ← 第一行必调
        // ... 调 EF DbContext ...
    }
}
```

**绝对禁止**：

- 手写 `[Route(...)]` / `[HttpGet(...)]` 路由 attribute（路由在 abstract base，NSwag 已生成）；
- 手写 `HttpClient.GetAsync(...)` 调其他服务（直接实现 abstract，框架绑定）；
- 漏调 `TenantGuard.VerifyPathTenant(tenantId)`（`§3.4`）。

### 3.3 持久层（AppDbContext + 共享 PG）

> 2026-08-30 落地：原本 `§3.3 InMemoryStore（scaffold fixture）` 的整节已被 AppDbContext 取代。EF Core 8 + Npgsql 读 shared PG，schema 由 `../saas-identity-platform-shared/sql/migrations/V001..V016.sql` 单一来源管理。`Program.cs` 启动不调 `Database.Migrate()`（shared SQL 已在部署前由 saas-nextjs `scripts/seed-db.mjs` 应用）。EF migrations 目录 `src/Migrations/` 提供 `InitialSchema` 镜像，用于 `scripts/check-ef-mirrors-sql.sh` 做 EF↔SQL diff。

### 3.4 TenantGuard 安全层

`src/Security/TenantGuard.cs`：

```csharp
public void VerifyPathTenant(string pathTenantId)
{
    var jwtTenantId = _context.CurrentTenantId();

    // Dev 兜底：JWT 没 claim 时信 path。
    if (string.IsNullOrEmpty(jwtTenantId))
    {
        if (_env?.IsDevelopment() == true) return;
        throw new UnauthorizedAccessException(
            $"tenant mismatch: path={pathTenantId} jwt={jwtTenantId}");
    }

    if (pathTenantId != jwtTenantId)
    {
        throw new UnauthorizedAccessException(
            $"tenant mismatch: path={pathTenantId} jwt={jwtTenantId}");
    }
}
```

**职责**：每个 tenant-scoped endpoint（路径含 `{tenantId}`）方法体的第一行必须调 `VerifyPathTenant(string)`：

- 读 JWT `tenant_id` claim（`TenantContext.CurrentTenantId()`）；
- 不匹配 throw `UnauthorizedAccessException`；
- dev 环境 JWT 缺 claim 时**兜底信 path**（让本地能跑通 MSW/dev-helper 的 `alg=none` test token，但 prod 路径始终走 HS256 真签 JWT）；
- prod 缺 claim 直接 throw（绝不兜底）。

**`TenantContext` 配套**：

```csharp
public virtual string? CurrentTenantId()
{
    var user = _accessor?.HttpContext?.User;
    return user?.FindFirst("tenant_id")?.Value;
}
```

virtual 方法便于测试 stub（`tests/StubTenantContext.cs`）。

**为什么是 `UnauthorizedAccessException` 而不是 `403`**：与 [父仓 §3.4 OAuth 2.0 + JWT 契约](../../docs/ARCHITECTURE.md) 一致 —— 所有后端的 OAuth 错误语义用同一组异常类型分流。`Program.cs::UseExceptionHandler` 把 `UnauthorizedAccessException` 映射到 `401 UNAUTHORIZED` JSON body（见 [§3.5](#35-programcs-配置)）。

### 3.5 Program.cs 配置

`src/Program.cs` 关键段（按职责）：

| 段 | 行号附近 | 职责 |
|---|---|---|
| ContentRoot | 13-17 | 强制 `ContentRootPath = AppContext.BaseDirectory` —— 任意 cwd 都能加载 appsettings（仓根 csproj 显式 Include + CopyToOutput） |
| JwtBearer 单配置 | 20-55 | `TokenValidationParameters` HS256 真验签 (Phase 2B 起统一 dev/prod 路径，无 dev 兜底分支) |
| CORS | 63-71 | `NextDev` policy，allowlist 来自 `Saas:Cors:AllowedOrigins`（默认 :5101/:5102/:5103/:5201） |
| DI 注册 | 73-79 | `AddSingleton<TenantContext>()` + `AddSingleton<TenantGuard>()` + `AddSingleton<JwtIssuer>()` |
| EF Core + Npgsql | 81-94 | `EnableDynamicJson()`（Npgsql 8 必需）；DbContext 用 `__ef_migrations_history` 表 |
| Controllers + ApplicationPart | 96-100 | `AddApplicationPart(typeof(...Controllers.Generated.AdminAppsControllerBase).Assembly)` 把 NSwag 产物所在 assembly 加进来 |
| Swagger UI | 102-115 + 133-138 | `/swagger` 暴露 OpenAPI 文档（前端 orval 复核 + QA curl 试验用） |
| 11 concrete controllers | 117-127 | 逐个 `AddScoped<...>()`（NSwag 产物是 abstract base，必须手注册 concrete） |
| `UseExceptionHandler` | 147-167 | `UnauthorizedAccessException → 401 UNAUTHORIZED`；`ArgumentException → 400 INVALID_REQUEST`；其他 → `500 INTERNAL_ERROR` |
| health | 169 | `GET /health → { status: "ok" }`（deploy 健康探针） |

**JwtBearer dev 分支详解**（`Program.cs:42-54`）：

```csharp
// Removed in v0.2.1 Phase 2B — RequireSignedTokens=false dev branch was deleted.
// JwtBearer now uses the standard TokenValidationParameters for HS256 验签 in dev + prod.
```

> **已删除** (Phase 2B) —— 标准 JwtBearer 8 安全硬编码拒收 `alg=none`，v0.2.1 起统一走 HS256 真验签，无 dev 兜底分支。

**prod 切换路径**（与 springboot 对称）：

- 删 dev 分支（保留上面 24-34 行 `TokenValidationParameters` 配置）；
- env-file 配 `Jwt:Authority`（JWKS URL）；
- 删 dev fallback secret `"dev-key-32-bytes-minimum-length!"`。

---

## 4. 核心流程

### 4.1 启动 → NSwag 重生 → 手写实现 → 测试

```
1. 改 shared TypeSpec?
   └─ cd ../saas-identity-platform-shared && npm run emit:openapi
       ↓ git commit + push

2. 重生本仓 Controllers + DTO?
   └─ bash scripts/gen-shared.sh
       └─ (cd ../shared && npm run emit:openapi)
       └─ nswag run aspnetcore.nswag   ← 重写 src/Controllers/Generated/Controllers.cs
       └─ cp shared/sql/migrations/V*.sql ./Migrations/  ← EF 镜像起点
       └─ bash scripts/check-ef-mirrors-sql.sh  ← diff EF ↔ shared SQL（ADR-0010）

3. 改 concrete Controller 业务逻辑?
   └─ src/Controllers/Implementation/<Tag>Controller.cs
       └─ 第一行调 _guard.VerifyPathTenant(tenantId)
       └─ 调 EF DbContext.Xxx（运行时已无 InMemoryStore）

4. dotnet build
   └─ dotnet test      ← xUnit，顺序跑（[assembly: CollectionBehavior(DisableTestParallelization)])
       └─ trace_cmd 产 .state/trace.json（fn-ID → 命中/跳过）

5. python scripts/gate.py -p saas-identity-platform-aspnetcore
   └─ L0 结构 → L1 格式 → L2 静态 → L3 类型/编译 → L4 测试 → L5 引用完整性
       ↓ exit 0
```

### 4.2 一个 tenant-scoped 请求的生命周期

```
浏览器 / 前端 fetch
  └─ Authorization: Bearer <jwt>        ← 前端 orval + axios 注 baseURL 发起
      ↓ ASP.NET Core JwtBearer middleware
      └─ TokenValidationParameters      ← HS256 真验签 (Phase 2B 起统一路径)
          └─ ClaimsPrincipal.User 装载
              ↓ MapControllers
              └─ TenantUsersController.ListUsersAsync(tenantId, ...)
                  └─ 第 1 行：_guard.VerifyPathTenant(tenantId)
                      └─ TenantContext.CurrentTenantId() → jwt "tenant_id" claim
                          └─ path tenantId == jwt tenantId ? 继续 : throw UnauthorizedAccessException
                              ↓
                              └─ _db.Users.Where(u => u.TenantId == tenantId)
                                  └─ 返回 JSON
```

**错误分流**（`Program.cs::UseExceptionHandler`）：

| 异常类型 | HTTP status | error code |
|---|---|---|
| `UnauthorizedAccessException`（TenantGuard 抛 / OAuth INVALID_GRANT） | 401 | `UNAUTHORIZED` |
| `ArgumentException`（参数校验 / OAuth INVALID_REDIRECT_URI） | 400 | `INVALID_REQUEST` |
| 其他 | 500 | `INTERNAL_ERROR` |

> **为什么错误分流到 JSON 而不是默认 500 空 body**：lab 后端 `EnsureSuccessStatusCode` 只能看到裸 500，OAuth `INVALID_SCOPE` / `INVALID_CLIENT` 不分流会假象成网络/DB 故障（Program.cs:144-147 注释）。

---

## 5. 与契约仓同步

### 5.1 共享契约仓关系

```
saas-identity-platform-shared/  (TypeSpec + SQL)
├── tsp/main.tsp                       ← API 契约真源
├── sql/migrations/V*.sql              ← DB schema 真源（双 SSOT, ADR-0007）
└── generated/openapi/openapi.yaml     ← emit 产物（git tracked）

saas-identity-platform-aspnetcore/    (本仓)
├── scripts/gen-shared.sh              ← 调 shared emit + 本地 NSwag
├── aspnetcore.nswag                   ← NSwag 配置（documentGenerator.fromDocument.url）
└── src/Controllers/Generated/         ← NSwag 产物（gitignored）
```

**绝对禁止**：把 `@saas/identity-platform-shared` 列为 NuGet / 项目依赖（NSwag 直接读相对路径 `../shared/generated/openapi/openapi.yaml`）——避免循环依赖 + 让本仓升级时不被 shared 仓的 npm 版本锁住。

### 5.2 改契约 → 三端同步（codegen 链）

```
[shared] 改 tsp/main.tsp 或 sql/migrations/V00N+1__*.sql
  ↓ git commit + push
[shared] npm run build  ← emit:openapi + tsc --noEmit
[本仓] bash scripts/gen-shared.sh  ← (cd ../shared && npm run emit:openapi) + nswag run + cp SQL
[本仓] 检查 NSwag 重生后 abstract method 列表变化
  └─ 新增?  → 在 src/Controllers/Implementation/<Tag>Controller.cs 加 override
  └─ 删除?  → 删对应 override + 检查测试 fnTest 引用
  └─ DTO 字段变化? → 检查 EF entity 字段名 + 测试断言（InMemoryStore 已删 2026-08-30）
[本仓] dotnet test
[父仓] git update-index --add --cacheinfo 160000,<NEW_HASH>,output/saas-identity-platform-aspnetcore
```

**关键检查点**：

- 改契约时必须**先**改 shared BASE tree 的 F 级（[ADR-0003](../../docs/adr/0003-function-tree-requires-human-approval.md)），再改各仓 I 级子项；
- `gen-shared.sh` 拷 SQL 前必须 cp（**不**做 diff abort，因为本仓 `Migrations/` 是 EF mirror 起点，详细策略见 [§6](#6-adr-0010-ef-core-migrations-待落地)）；
- 跨仓同步必须**同一批 commit**推完，避免一边指针新、一边指针旧的不一致窗口。

---

## 6. ADR-0010 EF Core Migrations 待落地

**当前状态**：本仓 `Migrations/` 目录**尚不存在**。ADR-0010 已 `Accepted`（[0010-aspnetcore-ef-mirrors-sql.md](../../docs/adr/0010-aspnetcore-ef-mirrors-sql.md)），但 InitialSchema 落地是 open question。

### 6.1 ADR-0010 决策摘要

| 维度 | 内容 |
|---|---|
| 决策 | 选 EF Core Migrations（备选 DbUp / Flyway for .NET 都被拒） |
| 代价 | DDL 双写（shared SQL + EF migration 类）；任何漂移 CI 红 |
| SSOT | `saas-identity-platform-shared/sql/migrations/*.sql` 仍是真源；EF migration 类是「强类型镜像」 |
| 启动路径 | **不**调 `Database.Migrate()`（避免 EF 自动跑第二次 + 多实例 race） |
| 启动校验 | `db.Database.OpenConnection()` + 查 `information_schema.tables` 验证 expected tables 存在；不匹配 throw |
| CI 钩子 | `scripts/check-ef-mirrors-sql.sh` 跑 `dotnet ef migrations script --no-transactions`，与 shared SQL 拼接后 diff；**diff 不为空 → exit 1** |
| strip 脚本 | EF 输出含 `BEGIN/COMMIT` / `SET client_min_messages` / `IF EXISTS` 包装，需 `scripts/lib/strip-ef-wrapper.sql` 去包装再 diff |
| 列序校验 | PG 对列序不敏感，但 script 校验列名集合 + 列序（防 EF 列序颠倒假阳性） |

### 6.2 当前落地路径

```
[gen-shared.sh step 3]
  └─ mkdir -p Migrations/
  └─ cp ../shared/sql/migrations/V*.sql ./Migrations/
  └─ if [ -d Migrations ] && [ -n "$(ls -A Migrations/*.cs 2>/dev/null)" ]; then
       bash scripts/check-ef-mirrors-sql.sh  ← CI 校验
     else
       echo "首次落地；跑: dotnet ef migrations add InitialSchema --project src"
       echo "然后 git commit migrations/<timestamp>_InitialSchema.cs"
     fi
```

**Open Question**：是否补 `dotnet ef migrations add InitialSchema` + commit `Migrations/<timestamp>_InitialSchema.cs`？还是修订 ADR-0010 改用 DbUp（与 shared SQL 形态更接近）？

> 详见本仓 `.state/session.json` 的待办条目。

---

## 7. 决策索引

按主题分组。本仓特有 + 跨仓引用：

### 7.1 关于"数据真源"

| ADR | 主题 | 一句话 |
|---|---|---|
| [0007](../../docs/adr/0007-shared-sql-ssot.md) | shared 仓扩到双 SSOT | shared 仓同时是 API 契约 + DB schema 真源；ORM 只反射 |
| [0010](../../docs/adr/0010-aspnetcore-ef-mirrors-sql.md) | aspnetcore EF 应镜像 SQL | EF Core Migrations 应镜像 shared SQL DDL（**待落地 InitialSchema**） |
| [0009](../../docs/adr/0009-db-credentials-env.md) | DB 凭据走 env | env-file + deploy 烘焙；不写 connection string 硬编码 |

### 7.2 关于"端形态"

| ADR | 主题 | 一句话 |
|---|---|---|
| [0008](../../docs/adr/0008-nextjs-full-stack.md) | saas-nextjs 兼全栈 | saas-nextjs 扩为 Frontend + Backend + DB 同仓；新增 profile `nextjs-backend.toml` |
| [0012](../../docs/adr/0012-msw-as-http-server.md) | msw 仓升级为独立 HTTP 服务 | B 强度：Express + `@mswjs/http-middleware` 暴露为端口监听 |
| [0014](../../docs/conventions/multi-repo-family.md#4-后端配置env-driven-单-urladr-0014) | env-driven 单 URL | 废弃 runtime BackendMode 联合类型 + localStorage；改 env-driven 3 getter |

### 7.3 关于"谁来管什么"

| ADR | 主题 | 一句话 |
|---|---|---|
| [0001](../../docs/adr/0001-suite-owns-l0-and-l5.md) | suite 保留 L0 / L5 门 | suite 拥有结构与引用完整性门，项目不能声明 |
| [0002](../../docs/adr/0002-trace-json-as-cross-language-anchor-contract.md) | trace.json 是跨语言锚点 | 测试挂功能 ID 必须经 trace_cmd，禁止手写 |
| [0003](../../docs/adr/0003-function-tree-requires-human-approval.md) | 功能清单变更需人批 | 改 F/I 必须先提 `/tree-change` 提案 |

### 7.4 本仓特有决策

| 主题 | 位置 |
|---|---|
| v0.2.0 NSwag codegen 迁移（不再 cp `shared/generated/csharp/`） | `scripts/gen-shared.sh` 头部注释 |
| `Jwt:SigningKey` HS256 ≥32B 强约束 | `src/Security/JwtIssuer.cs:43-45` |
| 已删除 (Phase 2B)；MSW 现在真签 HS256，dev/prod 走同一 `TokenValidationParameters` | `src/Program.cs:42-54` 旧分支；v0.2.1 起删除 |
| 错误分流到 JSON（OAuth `INVALID_*` 不再裸 500） | `src/Program.cs:147-167` |
| `InMemoryStore` 是 `internal static` + `InternalsVisibleTo` 给 tests | **已删除 2026-08-30** |

---

## 8. 术语表

| 术语 | 含义 | 详细 |
|---|---|---|
| **NSwag codegen** | OpenAPI → C# Controllers + DTOs 的工具链 | `aspnetcore.nswag` 配置 + `nswag run` CLI |
| **abstract base** | NSwag 产物（`controllerStyle: Abstract`），含路由 attribute 与 `throw NotImplementedException` 的 abstract 方法 | `src/Controllers/Generated/Controllers.cs` |
| **partial class concrete** | 手写继承 abstract base，覆盖 abstract 方法 + 调 TenantGuard | `src/Controllers/Implementation/<Tag>Controller.cs` |
| **InMemoryStore** | **已删除 2026-08-30**；运行时全走 AppDbContext | （无） |
| **TenantGuard** | path `tenantId` vs JWT `tenant_id` claim 校验 | `src/Security/TenantGuard.cs`；dev 兜底信 path |
| **TenantContext** | 从 `IHttpContextAccessor` 读 JWT claims | `src/Security/TenantContext.cs` |
| **JwtIssuer** | HS256 access token 签发（Auth + Oauth 共用） | `src/Security/JwtIssuer.cs` |
| **dev JWT fixture** | dev-only `alg=none` + dev-placeholder sig 模拟 token（test/MSW 用） | prod profile 不加载 `RequireSignedTokens=false` 分支；prod 走 `JwtIssuer` HS256 真签 + JwtBearer 真验签 |
| **RequireSignedTokens=false** | JwtBearer 8 放行 unsigned token 的开关 | dev 分支唯一必须开关 |
| **`[assembly: CollectionBehavior(DisableTestParallelization)]`** | **已删除 2026-08-30** —— InMemoryStore 删了，并发约束随之解绑 | （无） |
| **`ApplicationPart`** | ASP.NET Core 把 NSwag 产物所在 assembly 注册进来 | `Program.cs:99-100` |
| **`InternalsVisibleTo` attribute** | **已删除 2026-08-30** —— InMemoryStore 删了，attribute 一并移除 | （无） |
| **`__ef_migrations_history` 表** | EF Core 迁移历史表（启动**不**用它跑 migrate） | `Program.cs:94` |
| **`EnableDynamicJson()`** | Npgsql 8 显式开启 `Dictionary<string,object?>` ↔ `jsonb` 动态映射 | `Program.cs:90` |
| **`ErrorApp`** | ASP.NET Core 错误分流中间件（OAuth `INVALID_*` → JSON body） | `Program.cs:147-167` |
| **ssot** | Single Source of Truth | shared 仓承担双 SSOT（API + DB） |

---

## 附录 A：与父仓 docs/ARCHITECTURE.md 的关系

本文件**不是父仓的副本**——它只 zoom in 到本仓：

| 主题 | 父仓 § | 本文件 § |
|---|---|---|
| 14 子仓全景 | [父仓 §1](../../docs/ARCHITECTURE.md) | — |
| 五种角色拓扑 | [父仓 §2](../../docs/ARCHITECTURE.md) | — |
| 双 SSOT（API + DB） | [父仓 §3.1](../../docs/ARCHITECTURE.md) | §5 |
| 一份契约，三套 codegen | [父仓 §3.2](../../docs/ARCHITECTURE.md) | §3.1, §5.2 |
| 后端 env-driven 单 URL | [父仓 §3.3](../../docs/ARCHITECTURE.md) | §3.5 |
| OAuth 2.0 + JWT（HS256）契约 + DevJwtDecoder 兜底 | [父仓 §3.4](../../docs/ARCHITECTURE.md) | §3.5 |
| 端口 + CORS | [父仓 §3.5 + §6](../../docs/ARCHITECTURE.md) | §3.5 |
| aspnetcore 后端模板 | [父仓 §4.4.2](../../docs/ARCHITECTURE.md) | §1-§3（本文件 zoom in） |
| 门禁链 | [父仓 §5.4](../../docs/ARCHITECTURE.md) | §4.1 |
| 12 份 ADR | [父仓 §7](../../docs/ARCHITECTURE.md) | §7 |
| 术语表 | [父仓 §8](../../docs/ARCHITECTURE.md) | §8 |

**读法**：新人先读 [父仓 ARCHITECTURE.md](../../docs/ARCHITECTURE.md) 30 分钟全景，再读本文件 §1-§3 进入本仓。

## 附录 B：与 springboot 后端仓的对照

本仓与 `saas-identity-platform-springboot` 共同实现同一份契约。对照表：

| 维度 | aspnetcore（本仓） | springboot |
|---|---|---|
| 运行时 | .NET 8 Kestrel :5104 | Spring Boot 3.4 Tomcat :5105 |
| 路由来源 | NSwag → `Abstract` controller | openapi-generator → `interface` controller |
| 业务实现 | `partial class` 继承 abstract base | `@RestController` 实现 interface |
| tenant 校验 | `TenantGuard.VerifyPathTenant(tenantId)` | `TenantGuard.verifyPathTenant(tenantId)` |
| dev JWT | `RequireSignedTokens=false` | `@Profile("dev") DevJwtDecoder` |
| CORS env | `Saas:Cors:AllowedOrigins`（`Program.cs`） | `SAAS_CORS_ALLOWED_ORIGINS`（`SecurityConfig.corsConfigurationSource()`） |
| EF / JPA | EF Core 8 + Npgsql + EFCore.NamingConventions | Spring Data JPA + Hibernate（Flyway-off） |
| DB SSOT | shared SQL + EF Migrations 镜像（ADR-0010 待落地） | shared SQL + sync-db 灌入 dev DB |
| 测试隔离 | `[assembly: CollectionBehavior(DisableTestParallelization)]` | `@DirtiesContext` per test class |
| 测试 fnTest 嵌入 | `[Fact, Fn("M01.F01.I01")]` | `@DisplayName("fn=M01.F01.I01 ...")` |
| 健康探针 | `GET /health` | `GET /actuator/health`（需 permitAll） |
| Swagger | Swashbuckle `/swagger` | springdoc-openapi `/swagger-ui.html` |
| JWT 共享 key | HS256 `Jwt:SigningKey`（与 nextjs/springboot 同） | HS256 `JWT_SIGNING_KEY`（同） |

**关键差异**：

- **测试并行**：本仓**已开并行**（2026-08-30 删 InMemoryStore 后解绑）；springboot `@DirtiesContext` 隔离但仍可并行；
- **DB 落地**：springboot 用 Hibernate `ddl-auto=none` + shared SQL 灌入；本仓 EF 镜像 SQL 是 ADR-0010 主线但 InitialSchema 尚未提交；
- **路由继承**：本仓 `partial class` 继承 NSwag abstract base（路由在 base）；springboot `@RestController implements Api`（路由在 interface）。

详见父仓 [§4.4 springboot 模板](../../docs/ARCHITECTURE.md) + [§4.4.2 aspnetcore 模板](../../docs/ARCHITECTURE.md)。

## 附录 C：相关约定 / 决策 / 文档

- 子仓 gitlink / 加减 / 回滚：[docs/conventions/submodule.md](../../docs/conventions/submodule.md)
- 多仓家族拓扑细则：[docs/conventions/multi-repo-family.md](../../docs/conventions/multi-repo-family.md)
- Tag 规约：[docs/conventions/tag.md](../../docs/conventions/tag.md)
- 12 份 ADR：[docs/adr/](../../docs/adr/)
- 编码细则（不入主上下文）：[docs/conventions/](../../docs/conventions/)
- 本仓入口：[CLAUDE.md](../../saas-identity-platform-aspnetcore/CLAUDE.md)
- 本仓功能清单：[docs/functions/function-tree.md](../../saas-identity-platform-aspnetcore/docs/functions/function-tree.md)
- 父仓 CLAUDE.md：[CLAUDE.md](../../CLAUDE.md)
- 父仓 ARCHITECTURE.md：[docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md)
- 跨仓经验教训（不入仓）：`~/.claude/.../memory/`
