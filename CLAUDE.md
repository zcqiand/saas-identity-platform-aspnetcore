# CLAUDE.md — SaaS身份平台ASP.NET-Core后端

> 书稿配套仓 + harness 门禁仓双身份。入口，不是手册。L0 门强制上限 60 行。
> 本仓为《（书稿信息待补）》案例（待补）的可运行配套工程，是书稿代码块的 **source of truth**。

## 1. 项目定位

SaaS 多租户多应用身份平台的 C# 后端。NSwag 读 shared OpenAPI 生成 abstract Controllers + DTO；
手写 partial 实现类承接业务逻辑；JwtBearer + tenant_id claim 校验。

## 2. 铁律

- **TDD**：先写失败测试 → 确认红 → 实现 → 确认绿 → commit
- **版本钉死**：依赖与 `version-lock.json` 的 `version_lock` 一致；不引入 lock 外的库
- **tag 即放行**：全量回归绿后打 `v<MAJOR>.<MINOR>.<PATCH>-<YYYYMMDD>`（如 `v0.2.2-20260826`）
- **功能清单是锚点**：改 function-tree 走 `/tree-change`；同 commit；废弃只改状态，编号不复用
- 禁止手写 Controller 路由（路由必须由 NSwag 生成）
- 禁止业务逻辑前漏调 `TenantGuard.VerifyPathTenant(tenantId)`
- 禁止手写 fetch/HttpClient（直接实现 abstract 方法）
- 禁止把 shared 列为依赖（NSwag 直接读相对路径）
- 禁止删 `src/Controllers/Implementation/` 的 concrete 类；禁止直接编辑 NSwag 产物
- 禁止修改 InMemoryStore 字段名（与 NSwag DTO 强绑定）
- 禁止测试并行运行（InMemoryStore 是 static fixture）

## 3. 技术栈与版本（钉死于 version-lock.json）

ASP.NET Core 8 + xUnit + JwtBearer + NSwag codegen。明细见 `version-lock.json`。

门禁命令见 `.harness/stack.json`。**不要改它来让门变松。**

## 4. 验收

- suite 根目录跑 `python scripts/gate.py -p saas-identity-platform-aspnetcore`
- 改了 shared → `bash scripts/gen-shared.sh` 再跑门禁

## 5. 指向别处

- 契约真源 → `../saas-identity-platform-shared`
- 核心基建：TenantContext / TenantGuard / InMemoryStore / gen-shared.sh
- 决策 → `docs/adr/`；细则 → `docs/conventions/`；待办 → `PLAN.md`；版本 → `CHANGELOG.md`

## 6. 工作循环

1. 改 concrete Controller → `src/Controllers/Implementation/<Tag>Controller.cs`
2. gate exit 1 修；exit 2 停下问人
3. `/handoff` 更新 `.state/session.json`
