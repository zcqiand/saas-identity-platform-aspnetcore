-- V007__indexes.sql
-- 集中补齐性能/查询索引（GIN on JSONB、外键复合、常用过滤列）
-- 单列 FK 索引已在 V001..V006 内联（hot-path），本 V 文件补「跨表查询优化」

-- 1. tenants: settings GIN 索引（按 JSONB key 内部查询用）
CREATE INDEX idx_tenants_settings_gin ON tenants USING gin (settings);

-- 2. users: email 跨租户查找（admin / SSO 场景）；保留 tenants.status 用于 join 过滤
CREATE INDEX idx_users_email_global ON users (email);

-- 3. tenant_memberships: 跨租户查某 user 的所有 membership（M00.F02 /me）
CREATE INDEX idx_memberships_user_id   ON tenant_memberships (user_id);
CREATE INDEX idx_memberships_tenant_id ON tenant_memberships (tenant_id);

-- 4. roles: 跨租户按 code 查（admin 视角）
CREATE INDEX idx_roles_code_global ON roles (code);

-- 5. audit_events: 时间范围扫描 + tenant + actor 三维（M06 列表/导出）
CREATE INDEX idx_audit_events_tenant_occurred ON audit_events (tenant_id, occurred_at DESC);
CREATE INDEX idx_audit_events_actor           ON audit_events (actor_user_id);
CREATE INDEX idx_audit_events_target          ON audit_events (target_user_id);
CREATE INDEX idx_audit_events_action          ON audit_events (action);
CREATE INDEX idx_audit_events_metadata_gin    ON audit_events USING gin (metadata);

-- 6. menus: 按 app + type 过滤（M08 admin 列表）
CREATE INDEX idx_menus_app_type ON menus (app_id, type);

-- 7. role_menu_grants: 按 menu_id 反查（EffectiveMenuNode 计算：用户 → role → menu_ids）
CREATE INDEX idx_role_menu_grants_menu_ids_gin ON role_menu_grants USING gin (menu_ids);

-- 8. tenant_memberships.role_ids: GIN（按 role_id 查找「此角色的所有 membership」）
CREATE INDEX idx_memberships_role_ids_gin ON tenant_memberships USING gin (role_ids);

-- 9. api_keys: prefix 全平台查（admin 视角 / 误用排查）
CREATE INDEX idx_api_keys_prefix_global ON api_keys (prefix);

COMMENT ON INDEX idx_tenants_settings_gin        IS 'GIN on JSONB settings; supports `settings @> {…}` queries';
COMMENT ON INDEX idx_users_email_global           IS '跨租户查 email；admin/SSO 场景；非 hot-path 但需要';
COMMENT ON INDEX idx_audit_events_tenant_occurred IS 'M06 列表 hot-path; tenant + time DESC';
COMMENT ON INDEX idx_audit_events_metadata_gin    IS 'GIN on JSONB metadata; 按 action 上下文键查询';
COMMENT ON INDEX idx_role_menu_grants_menu_ids_gin IS 'GIN on menu_ids UUID[]; 反查「此菜单被哪些 role 授权」';
COMMENT ON INDEX idx_memberships_role_ids_gin     IS 'GIN on role_ids UUID[]; 反查「此角色的所有 membership」';