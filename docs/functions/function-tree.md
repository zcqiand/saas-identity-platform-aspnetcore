# saas-identity-platform-aspnetcore 功能树

> 多租户 SaaS 身份管理。Phase B 起由 `tsp/main.tsp` 派生；本表为占位骨架。

## 模块总览

| ID  | 模块 | 业务域边界 | 状态 |
|-----|------|-----------|------|
| M00 | 租户管理 | 多租户 CRUD、跨租户切换 | 规划 |
| M01 | 用户管理 | tenant-scoped 用户 CRUD、角色分配 | 规划 |
| M02 | 角色权限 | tenant-scoped 角色、权限矩阵 | 规划 |
| M03 | SSO 登录 | 密码登录、OIDC 回调、登出 | 规划 |
| M04 | OAuth2 Provider | 平台级应用 CRUD、授权码/令牌流程 | 规划 |
| M05 | API Key 管理 | tenant-scoped Key 生命周期 | 规划 |
| M06 | 审计日志 | tenant-scoped 审计事件、留存策略 | 规划 |

## 功能级（M0x.F0y）

| ID       | 功能 | 类型 | 状态 |
|----------|------|------|------|
| M00.F01  | 租户 CRUD（平台 admin） | 接口 | 规划 |
| M00.F02  | 当前用户跨租户切换 | 接口 | 规划 |
| M01.F01  | 用户 CRUD（tenant-scoped） | 接口 | 规划 |
| M01.F02  | 用户角色分配与状态切换 | 接口 | 规划 |
| M02.F01  | 角色 CRUD（tenant-scoped） | 接口 | 规划 |
| M02.F02  | 权限绑定（角色↔权限矩阵） | 接口 | 规划 |
| M03.F01  | 密码登录与失败锁定 | 接口 | 规划 |
| M03.F02  | OIDC 回调与 IDToken 校验 | 接口 | 规划 |
| M03.F03  | 登出（本地清理 + 全局 SSO） | 接口 | 规划 |
| M04.F01  | OAuth 应用 CRUD（平台级） | 接口 | 规划 |
| M04.F02  | 授权码签发与令牌交换/刷新 | 接口 | 规划 |
| M05.F01  | API Key 生命周期（tenant-scoped） | 接口 | 规划 |
| M06.F01  | 审计事件查询（tenant-scoped） | 查询 | 规划 |
| M06.F02  | 审计留存策略 | 接口 | 规划 |
