-- V008__users_role_ids_and_drop_redundant_index.sql
-- 解决 saas_dev � msw fixture ↔ TypeSpec ↔ Drizzle 四方漂移债：
--
-- (a) users.role_ids 列：OpenAPI TypeSpec user.tsp:29 声明 `roleIds: string[]` required；
--     msw fixture users.json 5 条都带；Drizzle schema.ts:128 已定义；但 V002 没建这列——
--     saas_dev 表里没列、seed-db.mjs 注释被迫写"忽略 roleIds"。
--     本次 V008 补齐：ADD COLUMN + 配套 GIN 索引。
--     authoritative 仍在 tenant_memberships.role_ids（V002）；users.role_ids 冗余
--     是为前端按 tenantId 直接取该租户下角色列表省一次 join。
--
-- (b) tenants 重复 unique index：V001 line 29 `tenants_code_unique` 已是 UNIQUE 约束，
--     V001 line 50 又单独 `CREATE UNIQUE INDEX idx_tenants_code`——SQL 自身冗余，
--     Drizzle schema.ts:110-111 镜像过来同步冗余。DROP 冗余 index 保留约束。

-- 1. ALTER TABLE users ADD COLUMN role_ids
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS role_ids UUID[] NOT NULL DEFAULT ARRAY[]::UUID[];

-- 配套 GIN 索引，按 role_id 反查 user 列表加速（adr-0007 §5）
CREATE INDEX IF NOT EXISTS idx_users_role_ids_gin ON users USING GIN (role_ids);

-- 2. DROP 冗余 unique index（保留 tenants_code_unique 约束即可）
DROP INDEX IF EXISTS idx_tenants_code;

COMMENT ON COLUMN users.role_ids IS '冗余列：authoritative 在 tenant_memberships.role_ids（V002）。本列便于按 tenantId 直查角色列表。V008 补齐（OpenAPI/msw/drizzle 此前已声明此字段，SQL 漂移）。';
