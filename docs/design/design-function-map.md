# 设计与功能对齐 — SaaS 身份平台ASP.NET Core后端 （已废段镜像豁免，9/7 迁移前快照）

> 人填、人评审。机器只检查功能 ID 存在性。
> 回答一个问题：**这个功能子项，落到哪段代码、哪张表、哪个权限码上？**
> 答不上来的行，说明设计没做完，别开工。

## 映射表

| 功能子项 ID | 页面/组件 | 接口 | 数据表 | 权限码 | 设计稿 | 状态 |
|---|---|---|---|---|---|---|
| M04.F03.I01 | OauthController#Authorize / OauthService#Authorize | POST /api/v1/oauth/authorize | OauthCode 实体（镜像 V014 oauth_codes，存 saas-code-{ts}-{rand}，TTL 10min）+ App（client_id/redirect_uris/scopes 校验） | M04.F03.I01 | - | 已上线 |
| M04.F03.I02 | OauthController#Token / ExchangeAuthorizationCode | POST /api/v1/oauth/token（grant_type=authorization_code） | OauthCode（验未消费/未过期/redirectUri 一致 → 标 consumed）+ App | M04.F03.I02 | - | 已上线 |
| M04.F03.I03 | OauthController#Token / RotateRefreshToken | POST /api/v1/oauth/token（grant_type=refresh_token） | OauthCode（refresh_token TTL 7d，旋转换发：旧 consumed 新写入） | M04.F03.I03 | - | 已上线 |
| M04.F03.I01 | OauthController#Authorize / OauthService#Authorize | POST /api/v1/oauth/authorize | OauthCode 实体（镜像 V014 oauth_codes，存 saas-code-{ts}-{rand}，TTL 10min）+ App（client_id/redirect_uris/scopes 校验） | M04.F03.I01 | - | 已上线 |
| M04.F03.I02 | OauthController#Token / ExchangeAuthorizationCode | POST /api/v1/oauth/token（grant_type=authorization_code） | OauthCode（验未消费/未过期/redirectUri 一致 → 标 consumed）+ App | M04.F03.I02 | - | 已上线 |
| M04.F03.I03 | OauthController#Token / RotateRefreshToken | POST /api/v1/oauth/token（grant_type=refresh_token） | OauthCode（refresh_token TTL 7d，旋转换发：旧 consumed 新写入） | M04.F03.I03 | - | 已上线 |

### 孤儿功能（declared_gap 豁免清单，2026-09-09 ADR-0028）

| 功能 ID | 名称 | 类型 | 映射 | 状态 |
|---|---|---|---|---|
| M01.F01.I02 | 创建用户 | 接口 | TenantUsersController#UsersPost | 已上线（9/7 镜像删除，本仓设计文档保留追溯） |
| M05.F01.I05 | 物理删 API Key | 接口 | TenantApiKeysController#ApiKeysDelete | 已废（目标 DDL 不再含 api_keys） |
| M09.F02.I02 | 设置角色菜单 | 接口 | TenantRoleMenusController#MenusPut | 已迁（→ M00.F04.I03） |
| M09.F03.I04 | app 分组映射 | 接口 | MeController#Menus | 已迁（→ M04.F04.I08） |

> 签发统一走 JwtIssuer（HS256，AuthController 与 OauthController 共用 IssueAccessToken）。
> 本仓其余已上线条目的设计映射待补（v0.2.x 前的 M00/M01 走通用 auth 链路），
> 本批次只登记 v0.2.0 Phase 6 真 OAuth 三接口 —— 对应 L5 软告警清零。
