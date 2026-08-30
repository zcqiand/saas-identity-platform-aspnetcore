-- V001__init_tenants.sql
-- 落地 entities: tenants (+ settings JSONB)
-- TypeSpec 来源: tsp/models/tenant.tsp Tenant / TenantStatus / TenantSettings
-- ADR-0007：shared/sql/migrations/ 是 DB 持久层 SSOT；三端 ORM 镜像本文件
-- 命名约定：表名复数 snake_case，列名 snake_case；FK 列名 `<entity>_id`
-- 枚举：PG 原生 CREATE TYPE；ORM 用 `@JdbcTypeCode(SqlTypes.NAMED_ENUM)` / `MapEnum<>` 镜像

-- 1. 启用 UUID 生成扩展
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- 2. 枚举类型
CREATE TYPE tenant_status AS ENUM (
    'active',
    'suspended',
    'archived'
);

-- 3. 主表 tenants
CREATE TABLE tenants (
    id          UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    code        VARCHAR(64)  NOT NULL,
    name        VARCHAR(255) NOT NULL,
    status      tenant_status NOT NULL DEFAULT 'active',
    settings    JSONB        NOT NULL DEFAULT '{}'::jsonb,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- code 平台内唯一
    CONSTRAINT tenants_code_unique UNIQUE (code),

    -- settings 必为 JSON object（不允许顶层数组/字符串）
    CONSTRAINT tenants_settings_is_object CHECK (settings IS NOT NULL AND jsonb_typeof(settings) = 'object')
);

-- 4. 自动维护 updated_at
CREATE OR REPLACE FUNCTION trg_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER tenants_set_updated_at
    BEFORE UPDATE ON tenants
    FOR EACH ROW
    EXECUTE FUNCTION trg_set_updated_at();

-- 5. 索引（V001 内联必要的；其余集中在 V007）
CREATE UNIQUE INDEX idx_tenants_code ON tenants (code);

COMMENT ON TABLE  tenants IS              'Multi-tenant root entity. Platform-level (admin) manages these. ADR-0007 SQL SSOT.';
COMMENT ON COLUMN tenants.settings IS     'JSONB opaque; runtime parser maps to TypeSpec TenantSettings { themeColor, locale, maxUsers }';
COMMENT ON COLUMN tenants.status IS       'PG-native enum; mirrors TypeSpec TenantStatus (active|suspended|archived)';