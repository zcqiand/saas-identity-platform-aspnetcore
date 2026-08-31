# 设计与功能对齐 — SaaS 身份平台ASP.NET Core后端

> 人填、人评审。机器只检查功能 ID 存在性。
> 回答一个问题：**这个功能子项，落到哪段代码、哪张表、哪个权限码上？**
> 答不上来的行，说明设计没做完，别开工。

## 映射表

| 功能子项 ID | 页面/组件 | 接口 | 数据表 | 权限码 | 设计稿 | 状态 |
|---|---|---|---|---|---|---|
| M04.F03.I07 | OauthController#Authorize / OauthService#Authorize | POST /api/v1/oauth/authorize | OauthCode 实体（镜像 V014 oauth_codes，存 saas-code-{ts}-{rand}，TTL 10min）+ App（client_id/redirect_uris/scopes 校验） | M04.F03.I07 | - | 已上线 |
| M04.F03.I08 | OauthController#Token / ExchangeAuthorizationCode | POST /api/v1/oauth/token（grant_type=authorization_code） | OauthCode（验未消费/未过期/redirectUri 一致 → 标 consumed）+ App | M04.F03.I08 | - | 已上线 |
| M04.F03.I09 | OauthController#Token / RotateRefreshToken | POST /api/v1/oauth/token（grant_type=refresh_token） | OauthCode（refresh_token TTL 7d，旋转换发：旧 consumed 新写入） | M04.F03.I09 | - | 已上线 |
| M01.F01.I02 | TenantUsersController#UsersPost | POST /api/v1/tenants/{tenantId}/users | users（Status="active" 默认, TenantUsersController.cs:103）+ tenant_memberships | M01.F01.I02 | - | 已上线 |
| M05.F01.I05 | TenantApiKeysController#ApiKeysDelete | DELETE /api/v1/tenants/{tenantId}/api-keys/{keyId} | api_keys（hard delete，无 audit；404 by FirstOrDefaultAsync + throw KeyNotFoundException → Program.cs mapping） | M05.F01.I05 | - | 已上线 |
| M09.F02.I02 | TenantRoleMenusController#MenusPut | PUT /api/v1/tenants/{tenantId}/roles/{roleId}/menus | role_menu_grants（PK role_id upsert） | M09.F02.I02 | - | 已上线 |
| M09.F03.I04 | MeController#Menus（装配第三步 app 分组映射） | GET /api/v1/me/menus | apps + menus（按 app.code 输出 Map<appCode, List<EffectiveMenuNode>>） | M09.F03.I04 | - | 已上线 |

> 签发统一走 JwtIssuer（HS256，AuthController 与 OauthController 共用 IssueAccessToken）。
> 本仓其余已上线条目的设计映射待补（v0.2.x 前的 M00/M01 走通用 auth 链路），
> 本批次只登记 v0.2.0 Phase 6 真 OAuth 三接口 —— 对应 L5 软告警清零。
