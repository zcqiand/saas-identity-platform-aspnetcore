# REQ-2026-001 后端根路径默认跳转 Swagger

| 项 | 值 |
|---|---|
| 提出人 | 用户 |
| 提出日期 | 2026-10-02 |
| 优先级 | P2 |
| 状态 | 待评审 |
| 关联 ADR | ADR-0027（消费树 ⊆ BASE subset invariant，本需求据以不登记功能树，见 §4） |

## 1. 需求描述

**用户原话（照抄）**：

> 所有后端增加一个，后端域名访问默认跳到swagger页面

**我的理解**：

本仓（saas-identity-platform 家族 4 后端之一，ASP.NET Core 8 栈）在根路径增加浏览器友好默认入口：
匿名访问后端域名根路径时跳转到本仓 Swagger UI（Swashbuckle，`RoutePrefix = "swagger"` → `/swagger`），
方便 QA 联调与契约复核从域名直达 API 文档。经澄清（见澄清记录）：

1. 范围 = 两家族全部 8 个后端仓，本文件是本仓一仓的落点；
2. 本仓 Swagger UI 现状：**已有**。Program.cs 已挂 `UseSwagger()` + `UseSwaggerUI()`，UI 在 `/swagger`；
3. 跳转全环境生效（dev/test/prod 同姿态，与 Program.cs 注释「prod 暂全开」的家族现状一致）；
4. 不进契约面、不同步 contract-test 仓（与 /health 同类基础设施端点）；
5. 不登记功能树（架构约束见 §4）。

### 澄清记录

| 疑问 | 澄清结论 | 澄清人 | 日期 |
|---|---|---|---|
| 「所有后端」范围？suite 有两家族 × 4 栈共 8 仓 | 两家族全部 8 仓都做 | 用户 | 2026-10-02 |
| 个别仓没有 Swagger 页可跳怎么办（缺页仓：lab-springboot / 双 rails） | 一并补齐 Swagger UI 再挂跳转，字面义完整实现（本仓已有页，仅挂跳转） | 用户 | 2026-10-02 |
| 跳转在哪些环境生效 | 所有环境统一跳（与现状 Swagger 全环境暴露一致） | 用户 | 2026-10-02 |
| 是否按硬规则同步 contract-test 仓 | 不同步：不在 .tsp 契约面内，与 /health 同类（/health 从未进 CT 断言） | 用户 | 2026-10-02 |
| 工作流要求「新增功能先登记功能树」，本需求如何登记？ | 不登记。基础设施端点进树违反 ADR-0027 subset invariant（消费树必须 ⊆ BASE，extra 即红），BASE 侧「仅后端」交付行又会让前端树违反交付列收口；先例：infra 专属模块段（原 97/98 号段）已全家族退役出树，/health 从未进树。架构判定，随文档送用户追认 | Claude | 2026-10-02 |

## 2. 验收标准

| 编号 | 场景（给定） | 操作（当） | 预期（则） |
|---|---|---|---|
| AC-1 | 后端进程已启动 | 匿名 GET 根路径 `/` | 301/302 跳转到 `/swagger`，跟随跳转后 Swashbuckle UI 200 可渲染 |
| AC-2 | 任意环境（dev/test/prod 同姿态） | 重复 AC-1 | 行为一致；带不带 Authorization 行为一致 |
| AC-3 | 既有面回归 | 访问任意 `/api/v1/*` 契约端点与 `/health`；注意 saas session cookie 中间件在管道内 | 行为与改动前完全一致（跳转只占根路径，且在 SaasSessionMiddleware 语义之外——不设不读会话） |
| AC-4 | openapi 文档面纯净 | GET `/swagger/v1/swagger.json` | 跳转端点**不出现**在文档里（中间件实现或 `[ApiExplorerSettings(IgnoreApi = true)]`） |
| AC-5 | 门禁 | 跑本仓 L1-L4 | 全绿；contract-test 仓 gate 回归不受影响 |

## 3. 任务拆解

| 任务 ID | 任务描述 | 类型 | 负责人 | 预估 | 状态 |
|---|---|---|---|---|---|
| T-1 | 根路径跳转：`/` → `/swagger`（重定向中间件或 MapGet，挂在管道早段、匿名、不进 ApiExplorer、不触碰 saas session cookie） | 开发 | 待定 | XS | 待开始 |
| T-2 | L1-L4 门禁回归 + curl 三验（302、跟随 200、/api/v1/* 不回归） | 门禁 | 待定 | XS | 待开始 |

## 4. 功能影响（需求与功能对齐的唯一位置）

**无 —— 本需求不登记功能树（影响功能数 0）。**

定性：根路径跳转与 Swagger UI 暴露是**基础设施端点**，不是产品功能面。三重依据：

1. **ADR-0027 subset invariant**：消费仓功能树必须 ⊆ shared BASE，BASE 外 M/F/I 即门禁红，
   vertical_extra 白名单已按 ADR-0027 终态全家族归零；BASE 外 ID 必须先 tree-change 进 BASE。
2. **BASE 侧也不可进**：BASE 行按 ADR-0024 §3 全家族照抄，「仅后端」交付行会让前端树
   违反前端树交付列收口（前端树只准 仅前端/前端+后端）。后端专属基础设施在 BASE 无合法挂位。
3. **先例**：infra 专属模块段（原 97/98 号段）已全家族退役出树，/health 从未进树。
   本需求与 /health 同类，同待遇。

功能影响表因此为空——没有 ID 即无悬空引用，不违反「ID 必须存在于功能树」。

## 5. 流程影响

无。

## 6. 风险与回滚

| 风险 | 影响面 | 缓解 | 回滚方式 |
|---|---|---|---|
| 中间件顺序不当（撞 SaasSessionMiddleware / CORS 语义） | 路由/会话异常 | 挂在 UseRouting 之后；AC-3 回归断言兜底 | revert 跳由 commit，恢复 404 现状 |
| prod 暴露面认知：裸域名直达 API 文档 | 安全姿态认知 | Swagger 本就全环境暴露（Program.cs 注释「prod 暂全开」），本需求不改变暴露姿态；后续 prod 收口时跳转随 UI gating 同步收口 | revert |

无数据面、无契约面变更。
