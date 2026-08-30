-- V009__fix_lab_mgmt_app_id.sql
-- 修复 V015 的 menus_app_fk / rmg_role_fk 外键违规（2026-08-29 saas-nextjs 部署事故）。
--
-- 根因（两处同构）：线上 saas_prod 的 apps / roles 行是早期运行时 seed
-- （saas-nextjs src/seeds/*.json）灌的，id 是随机 UUID：
--   * apps: lab-management 行 id=5b6a189b-...，V014 收敛 client_id 后
--     ON CONFLICT (client_id) DO NOTHING 跳过 INSERT → 无 id=11111111-... 行
--     → V015 menus.app_id 外键炸（menus_app_fk）
--   * roles: tenant-001 已有 admin（id=89b609f4-...），V015 的
--     ON CONFLICT (tenant_id, code) DO NOTHING 跳过 INSERT → 无
--     id=...a00000000001 行 → V015 role_menu_grants.role_id 外键炸（rmg_role_fk）
-- V014/V015 的「ON CONFLICT + 固定 id」组合只对全新库成立；对已有随机 id
-- 存量行的库，语义是「跳过插入」而非「归一 id」。
--
-- 编号说明：本文件必须排在 V015 之前（sync-db 按文件名字典序执行，
-- V016 编号会晚于 V015 执行 → 修复晚于爆炸，无效）。V009-V013 编号空洞
-- 从未使用（git 历史全量扫描确认），占用 V009 安全。
--
-- 修复（幂等，可重跑）：
--   1. memberships.role_ids 数组（无 FK 约束，不会级联）显式替换旧 role id
--   2. DELETE 旧随机 id 的 lab-management app 行（menus 27 条旧 seed 与
--      oauth_codes 48 条短命码经 CASCADE 连带清理，均为可弃数据）
--   3. DELETE tenant-001 的旧随机 id roles（role_menu_grants CASCADE 跟走，
--      V015 立即重灌）——注意先做第 1 步，防数组残留悬空引用
--   4. INSERT V014 同款标准 app 行（roles 由 V015 自己灌）
--   5. V015 由 sync-db 重放（其事务在历次事故中回滚、tracking 未记录）

-- 1. memberships.role_ids 数组归一（先于 DELETE roles，防悬空引用）
UPDATE tenant_memberships
SET role_ids = array_replace(role_ids,
    (SELECT id FROM roles WHERE tenant_id = '00000000-0000-0000-0000-000000000001' AND code = 'admin'
       AND id <> '00000000-0000-0000-0000-a00000000001'),
    '00000000-0000-0000-0000-a00000000001'::UUID)
WHERE tenant_id = '00000000-0000-0000-0000-000000000001'
  AND EXISTS (SELECT 1 FROM roles
              WHERE tenant_id = '00000000-0000-0000-0000-000000000001'
                AND code = 'admin'
                AND id <> '00000000-0000-0000-0000-a00000000001');

-- 2. 删旧 id 的 lab-management app（menus/oauth_codes CASCADE 跟走）
DELETE FROM apps
WHERE code = 'lab-management'
  AND id <> '11111111-1111-1111-1111-111111111111';

-- 3. 删 tenant-001 旧随机 id roles（role_menu_grants CASCADE 跟走，V015 重灌）
--    只动 V015 会重灌的码（admin/member）；未知自定义 role 不碰
DELETE FROM roles
WHERE tenant_id = '00000000-0000-0000-0000-000000000001'
  AND code IN ('admin', 'member')
  AND id <> '00000000-0000-0000-0000-a00000000001';

-- 4. 灌标准 app 行（镜像 V014 的 INSERT，防 V014 tracking 已记录不会重跑）
INSERT INTO apps (
    id, code, name, description, icon, sort_order, status,
    client_id, client_secret_hash, redirect_uris, scopes, grant_types,
    is_first_party, created_at, updated_at
) VALUES (
    '11111111-1111-1111-1111-111111111111',
    'lab-management',
    '建筑工程实验室管理系统',
    'lab-mgmt OAuth client — 3 个 saas 后端共用同一 app.id (= client_id = UUID)',
    'flask',
    100,
    'active',
    '11111111-1111-1111-1111-111111111111',
    'lab-mgmt-secret',
    ARRAY[
        'https://lab-vue.xiangru.uk/login',
        'https://lab-react.xiangru.uk/login',
        'https://lab-nextjs.xiangru.uk/login',
        'http://localhost:5173/login',
        'http://localhost:5174/login',
        'http://localhost:3001/callback'
    ]::TEXT[],
    ARRAY['lab.read', 'lab.write']::TEXT[],
    ARRAY['authorization_code', 'refresh_token']::oauth_grant_type[],
    TRUE,
    CURRENT_TIMESTAMP,
    CURRENT_TIMESTAMP
) ON CONFLICT (client_id) DO NOTHING;
