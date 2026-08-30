-- V015__seed_lab_mgmt_menus.sql
-- 落地 lab-management app 的菜单树 + role_menu_grants 种子。
--
-- 背景（2026-08-28 prod SSO 后 /api/auth/menus 503 事故）：
--   * saas-springboot /me/menus 此前是 stub（返回 {}），且全链路 migrations 无任何
--     menus / roles / role_menu_grants 种子 —— prod DB 是「三无」库，真实现了查询也是空树。
--   * 菜单内容真源镜像：lab-management-system-react/src/components/app/menus.ts
--     （6 组 26 项，code m-* 是跨仓锚点，与 data-fn M98.F04.<code> 对齐）。
--
-- 设计：
--   * 全部固定 UUID（00000000-0000-0000-0000-000000000000<NNNN> 命名空间），
--     role_menu_grants / memberships 引用不漂移；重跑幂等（ON CONFLICT DO NOTHING）。
--   * menus.app_id 挂 V014 seed 的 lab-management app（11111111-1111-...）。
--   * admin role（tenant 00000000-0000-0000-0000-000000000001）grant 全部 26 菜单。
--   * 该 tenant 下所有 active 用户挂上 admin role（修 prod alice roleIds:[] ——
--     membership 行已存在，只 UPDATE role_ids，不碰其他列）。
--   * nextjs 仓 seeds/*.json 是它自己的 seed 路径（drizzle 启动灌），与本 SQL 无关；
--     本文件经 gen-shared.sh 拷入 springboot migration 目录。aspnetcore 同步拷贝。

-- 1. menus 树（镜像 lab-react menus.ts；id 规则：00000000-0000-0000-0000-000000000000gNNN 组 / ...pNNN 页）
INSERT INTO menus (id, app_id, parent_id, code, name, path, icon, type, sort_order) VALUES
    -- 组 1 总览
    ('00000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', NULL, 'm-overview',  '总览',     NULL,                        'LayoutDashboard', 'group', 1),
    ('00000000-0000-0000-0000-000000000101', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000001', 'm-dashboard', '仪表盘', '', 'LayoutDashboard', 'page', 1),
    -- 组 2 基础数据
    ('00000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', NULL, 'm-basedata',  '基础数据', NULL,                        'Database',        'group', 2),
    ('00000000-0000-0000-0000-000000000201', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000002', 'm-models',      '型号维护',   'models',      'Database',    'page', 1),
    ('00000000-0000-0000-0000-000000000202', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000002', 'm-specifications', '规格维护', 'specifications', 'Database', 'page', 2),
    ('00000000-0000-0000-0000-000000000203', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000002', 'm-grades',      '等级维护',   'grades',      'Database',    'page', 3),
    ('00000000-0000-0000-0000-000000000204', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000002', 'm-brands',      '牌号维护',   'brands',      'Database',    'page', 4),
    ('00000000-0000-0000-0000-000000000205', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000002', 'm-contracts',   '合同管理',   'contracts',   'ClipboardList', 'page', 5),
    ('00000000-0000-0000-0000-000000000206', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000002', 'm-report-names', '报告名称维护', 'report-names', 'ScrollText', 'page', 6),
    ('00000000-0000-0000-0000-000000000207', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000002', 'm-param-interfaces', '参数界面维护', 'param-interfaces', 'Wrench', 'page', 7),
    -- 组 3 试验过程
    ('00000000-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111', NULL, 'm-process',   '试验过程', NULL,                        'FlaskConical',    'group', 3),
    ('00000000-0000-0000-0000-000000000301', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000003', 'm-receipts',       '接样管理', 'receipts',        'FlaskConical', 'page', 1),
    ('00000000-0000-0000-0000-000000000302', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000003', 'm-task-assignment', '任务分配', 'task-assignment', 'ClipboardList', 'page', 2),
    ('00000000-0000-0000-0000-000000000303', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000003', 'm-data-entry',     '数据录入', 'data-entry',      'TestTube2',    'page', 3),
    -- 组 4 检测能力
    ('00000000-0000-0000-0000-000000000004', '11111111-1111-1111-1111-111111111111', NULL, 'm-capability','检测能力', NULL,                        'Beaker',          'group', 4),
    ('00000000-0000-0000-0000-000000000401', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000004', 'm-inspection-specialties', '检测专项', 'inspection-specialties', 'Beaker', 'page', 1),
    ('00000000-0000-0000-0000-000000000402', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000004', 'm-inspection-standards',   '检测标准', 'inspection-standards',   'ScrollText', 'page', 2),
    ('00000000-0000-0000-0000-000000000403', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000004', 'm-inspection-parameters', '检测参数', 'inspection-parameters', 'Activity',  'page', 3),
    ('00000000-0000-0000-0000-000000000404', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000004', 'm-inspection-objects',    '检测项目', 'inspection-objects',    'PackageSearch', 'page', 4),
    ('00000000-0000-0000-0000-000000000405', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000004', 'm-inspection-technical-requirements', '技术要求', 'inspection-technical-requirements', 'Shield', 'page', 5),
    ('00000000-0000-0000-0000-000000000406', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000004', 'm-inspection-calculation-methods',   '计算方法', 'inspection-calculation-methods',   'Settings', 'page', 6),
    -- 组 5 报告
    ('00000000-0000-0000-0000-000000000005', '11111111-1111-1111-1111-111111111111', NULL, 'm-reports',   '报告',     NULL,                        'FileText',        'group', 5),
    ('00000000-0000-0000-0000-000000000501', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000005', 'm-report-issue',   '报告发放', 'report-issue',   'FileText',   'page', 1),
    ('00000000-0000-0000-0000-000000000502', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000005', 'm-report-review',  '报告审核', 'report-review',  'FileText',   'page', 2),
    ('00000000-0000-0000-0000-000000000503', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000005', 'm-report-approve', '报告批准', 'report-approve', 'FileText',   'page', 3),
    ('00000000-0000-0000-0000-000000000504', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000005', 'm-report-archive', '报告归档', 'report-archive', 'ScrollText', 'page', 4),
    -- 组 6 统计
    ('00000000-0000-0000-0000-000000000006', '11111111-1111-1111-1111-111111111111', NULL, 'm-stats',     '统计',     NULL,                        'ListChecks',      'group', 6),
    ('00000000-0000-0000-0000-000000000601', '11111111-1111-1111-1111-111111111111', '00000000-0000-0000-0000-000000000006', 'm-summary',        '报告汇总', 'summary',        'ListChecks', 'page', 1)
ON CONFLICT (app_id, code) DO NOTHING;

-- 2. 默认 tenant（replay 库 / 全新 saas DB 都没有 V001 之后的 tenant 行；幂等。
--    与 nextjs seeds/tenants.json 的 acme 同 id）
INSERT INTO tenants (id, code, name)
VALUES ('00000000-0000-0000-0000-000000000001', 'acme', 'Acme Corporation')
ON CONFLICT (code) DO NOTHING;

-- 3. admin role（tenant 00000000-...-001；PG 侧新 id，nextjs json seeds 的 '...-role-admin' 字符串 id 在 PG uuid 列不合法）
INSERT INTO roles (id, tenant_id, code, name, description)
VALUES ('00000000-0000-0000-0000-a00000000001',
        '00000000-0000-0000-0000-000000000001',
        'admin', '管理员',
        'lab-management 全菜单（V015 seed；镜像 nextjs seeds/roles.json）')
ON CONFLICT (tenant_id, code) DO NOTHING;

-- 3. role_menu_grants：admin → 全部 26 菜单（幂等重灌）
INSERT INTO role_menu_grants (role_id, tenant_id, menu_ids)
VALUES ('00000000-0000-0000-0000-a00000000001',
        '00000000-0000-0000-0000-000000000001',
        ARRAY(
            SELECT id FROM menus
            WHERE app_id = '11111111-1111-1111-1111-111111111111'
        )::UUID[])
ON CONFLICT (role_id) DO NOTHING;

-- 4. 该 tenant 现有 active membership 挂 admin role（修 prod alice roleIds:[]；
--    只在 role_ids 为空时补，尊重运维手工配置过的角色）
UPDATE tenant_memberships
SET role_ids = ARRAY['00000000-0000-0000-0000-a00000000001']::UUID[],
    joined_at = joined_at
WHERE tenant_id = '00000000-0000-0000-0000-000000000001'
  AND status = 'active'
  AND role_ids = ARRAY[]::UUID[];
