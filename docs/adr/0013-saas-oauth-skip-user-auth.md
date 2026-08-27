# ADR-0013 — saas OAuth 跳过用户认证（family 简化设计）

> 状态：**已批准**（路线 A — 完整修 OAuth）
> 批准日期：2026-08-27
> 决策人：family 维护者
> 关联 REQ：REQ-2026-020-oauth-session-real-auth.md
> 关联 PLAN：PLAN-2026-001-oauth-session.md

## 现象

prod 走完整 OAuth 流程时，`saas /login` 渲染后**不停留**（用户没机会输用户名密码），直接跳到 lab `?code=...&state=...` 触发 `/api/auth/sso/callback` 换 token。换 token **成功**——但**根本没走用户登录环节**。

## 链路

```
lab /login
  → GET /api/auth/sso/authorize
  → lab 后端调 saas-aspnetcore POST /api/v1/oauth/authorize
       { clientId, redirectUri, responseType:"code", scope, state, tenantId }
    → saas-aspnetcore 校验 clientId / redirectUri / scope，
      **直接 GenerateCode() 写 oauth_codes 表，返 code**（无用户认证）
  → lab 后端拼 saas-nextjs/login?code&redirect_uri&state
浏览器
  → 访问 saas-nextjs/login → **saas-nextjs 没有 login UI 路由**（404/被前端 fallback 跳走）
  → 浏览器被自动 302 回 lab /login?code&state
  → lab /api/auth/sso/callback
    → lab 后端调 saas-aspnetcore POST /api/v1/oauth/token
      { grantType, code, redirectUri, clientId, tenantId }
    → saas-aspnetcore ExchangeAuthorizationCode：
      - 校验 code 未消费 / 未过期 / redirectUri 一致
      - **直接 `Users.FirstOrDefaultAsync(u => u.TenantId == body.TenantId)`** 拿该 tenant 第一个 user
      - 签 token 给这个 user，**不检查用户名/密码/session**
    → 返 token 给 lab
  → lab 登录完成
```

## 根因

`saas-aspnetcore/src/Controllers/Implementation/OauthController.cs:136-138`：

```csharp
var user = await _db.Users.FirstOrDefaultAsync(u => u.TenantId == body.TenantId);
if (user == null) throw new ArgumentException("NO_USER: tenantId has no user");
// 直接发 token，无 username/password/session 校验
```

注释自承「v0.4.0：Phase 5 mock（Guid.NewGuid() + 字面量字符串）」+「dev 暂不验 clientSecret」。**整个 saas OAuth 是「假 OAuth」** —拿到 tenantId 就发 token 给人。

## 范围

- lab-aspnetcore：`/api/auth/sso/authorize` 拼 `LoginUrl` 时 `code` 已经在 URL 上，**用户根本没机会输密码**。修 lab 端不能根治。
- saas-nextjs：**没有 login UI**（无 `app/login/page.tsx`）。login UI 在 saas-vue / saas-react 仓，但 lab-aspnetcore deploy env `LAB_SSO_LOGIN_URL=https://saas-nextjs.xiangru.uk`，指错了仓。
- saas-vue / saas-react：有 login UI（`<form> username + password → POST /api/v1/auth/login`），但产物没人用。
- saas-aspnetcore：OAuth 端点简化设计，不查用户身份。

## 三个修法

### A. saas-aspnetcore 加真用户认证（家族根正）
- `/api/v1/oauth/authorize` 检查 saas session cookie，未登录返 401
- `/api/v1/oauth/token` 必须由已认证用户触发（请求带 saas session cookie 或 saas bearer，session 内有 user_id）
- saas-vue / saas-react login UI：用户输密码 → POST `/api/v1/auth/login` → 存 saas session cookie → 触发 `/api/v1/oauth/authorize` → 拿到 code
- 改 lab-aspnetcore `LAB_SSO_LOGIN_URL` 指向 saas-vue 或 saas-react 域名
- 涉及 4 仓改动 + saas session 管理设计

### B. lab 端改造（半步 root cause）
- lab `/api/auth/sso/authorize` 不预生成 code，直接 redirect 到 saas UI 的 `?response_type=code&client_id&redirect_uri&state`（不带 code）
- 让 saas UI 自己跑完整 OAuth
- 同样依赖 saas 真用户认证 + UI 存在

### C. 接受当前简化，加 dev 用户选择器
- saas-vue / saas-react login UI：加 dev 用户下拉（alice / bob / carol），提交只点按钮「模拟登录」
- 文档化「这是 dev mock，无真密码」
- 短期可走，覆盖 family 文档演示需求

## 推荐

按 family 多仓拓扑与「saas 是真身份平台」语义，**A 是治本**；但工作量大（4 仓 + session 设计）。

**C 是 dev 演示友好**；**B 是中间路线**，但仍依赖 A 的 session 检查。

## 决策闸

需要用户在以下选项中定：

- [ ] A：完整修 OAuth（saas-aspnetcore + saas-vue/react + lab-aspnetcore deploy env 重指向）
- [ ] B：只改 lab 端，saas 端写「TODO」标
- [ ] C：dev mock 模式，加用户选择器，文档明示「非真 OAuth」
- [ ] 暂不动：把现象记在这里，等 family 文档（ARCHITECTURE.md）补一节「Phase 5 mock OAuth 限制」

## 相关

- `output/lab-management-system-aspnetcore/src/Services/AuthService.cs:181` — `authorizeUrl` 拼接 `code=...` 的实现
- `output/lab-management-system-nextjs/src/app/api/auth/sso/authorize/route.ts:11-12` — 注释明示设计意图（同 lab-aspnetcore 同款 pre-code 模式）
- `output/saas-identity-platform-aspnetcore/src/Controllers/Implementation/OauthController.cs:136-138` — 根因：拿 tenantId 直发 token
- ADR-0009: saas 快照缓存 ADR-0008: 真后端 OAuth + JWT 签发