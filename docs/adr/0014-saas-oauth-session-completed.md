# ADR-0014 - saas OAuth 真用户认证收口（路线 A 完成）

> 状态：**已完成**
> 完成日期：2026-08-27
> 决策人：family 维护者
> 前置 ADR：[ADR-0013](0013-saas-oauth-skip-user-auth.md)（路线 A 批准）
> 关联 REQ：REQ-2026-020-oauth-session-real-auth.md
> 关联 PLAN：PLAN-2026-001-oauth-session.md（T-1 ~ T-12 全部完成）

## 结果

ADR-0013 诊断的「假 OAuth」（拿 tenantId 直发 token）已按路线 A 完整修复。
saas OAuth 链路现在要求**真实用户登录**：

```
用户 -> saas-vue/saas-react LoginPage (username/password)
     -> POST /api/v1/auth/login
          -> FailedLoginStore 锁定检查（5 错 / 15min，423）
          -> 验密码 -> 写 saasSession cookie (HttpOnly + SameSite=Lax + Path=/api)
          -> SaasSessionStore 进程内 session (24h TTL, 惰性过期清理)
浏览器 -> 带 cookie 调 /api/v1/oauth/authorize
     -> SaasSessionMiddleware 解析 cookie 注入 HttpContext.Items
     -> OauthController.Authorize 验 session (无/过期 -> 401)
     -> 发一次性 code (绑定 session 的 user/tenant)
     -> /api/v1/oauth/token 验 session + code -> user_id 从 session 注入
        (不再信 body.TenantId)
lab 前端 -> /api/me/menus 验 session + 角色授权
```

## 各仓落地

| 任务 | 仓 | 内容 |
|---|---|---|
| T-1~T-3 | saas-aspnetcore | SaasSessionStore / SaasSessionMiddleware / AuthController.Login + 失败锁定 |
| T-4~T-6 | saas-aspnetcore | Authorize / Token / Menus 验 session（user_id 注入） |
| T-7 | saas-msw | mock 同步：saas-session Map + cookie 解析 + 401 |
| T-8 | saas-vue | LoginPage 提交 username/password + 423 锁定提示 |
| T-9 | saas-react | 同款 + 423 锁定提示 |
| T-10 | lab-aspnetcore | deploy `LAB_SSO_LOGIN_URL` -> saas-react.xiangru.uk（原指 saas-nextjs，无 login UI - ADR-0013 根因之一） |
| T-11 | saas-aspnetcore | 集成测试：login -> cookie -> middleware -> authorize -> token -> me/menus 全链路 |
| T-12 | saas-msw | cookie jar 行为测试（Set-Cookie 断言 / 过期惰性清除 / 全链路） |

## 教训（写给后续 family 改造）

1. **msw node 的 Set-Cookie 屏蔽**：fetch API 层不暴露 HttpOnly Set-Cookie，测试须经 debug export（`saasSessionsForTest`）或手带 `Cookie:` 头模拟 jar。
2. **vi.hoisted 的 TDZ 陷阱**：hoisted 回调执行先于 import，里面不能调 `ref()`——用普通 `{ value }` 对象。
3. **cookie 标志大小写**：ASP.NET Core 序列化 `HttpOnly` 为小写 `httponly`，测试断言须 `OrdinalIgnoreCase`。
4. **Authorize 的 code 与 body.TenantId 绑定**：集成流程测试传 seed 租户，不是任意 Guid。
5. **指针别指没 UI 的仓**：deploy env 指向前先确认目标产物有对应路由（saas-nextjs 无 login 页）。

## 遗留（Phase 5+）

- `password_hash` 仍是 `plain:{password}` dev seed，真实换 argon2（M10.F04）
- SaasSessionStore 进程内存储，Phase 6+ 切 Redis（多副本部署必需）
- msw `/oauth/token` dev 阶段不严验 clientSecret（生产真后端验）
- PG Testcontainers 集成测试（M10.F04）落地后，解锁 AuthController 6 个 Skip 用例
