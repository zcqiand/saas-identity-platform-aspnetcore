# CHANGELOG — saas-identity-platform-aspnetcore

格式参照 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [0.3.29] — 2026-09-04

- 测试体系改造（v0.2.26 lab-aspnetcore Kw 翻译事故的同族预防）：
  - 新增 `MeMenusPgTest`：M09.F03 my-menus 查询链（membership.roleIds uuid[] →
    grants Contains → 菜单树父链补全 → apps JOIN）跑 saas_test 真 PG，
    硬依赖连不上即败不 skip；InMemory 版保留作快速路径。
  - 新增 `TestDb` 基建：dataSource builder 镜像 Program.cs
    （EnableDynamicJson + MapEnum 全套）——测试链单独配置 = 组合根盲区。
- fix(db): `membership_status` native enum 缺 HasConversion/MapEnum —— 真库测试
  首跑抓出 42804 潜伏 bug（当前代码只读 memberships 未触发；写路径一旦上线即炸）。
  新增 `MembershipStatusPg` + AppDbContext converter + Program.cs/TestDb MapEnum。
- ci.yml：L4 过滤 `Category!=RealDb`（CI=翻译性 / gate=真库分层，
  lab-aspnetcore v0.2.27 同款）。

## [0.2.2] — 2026-08-27

- 初始化台账：ASP.NET Core 8 后端。历史变更见 git log 与 `.state/session.json`。
