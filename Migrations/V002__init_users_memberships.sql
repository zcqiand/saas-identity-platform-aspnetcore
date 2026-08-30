-- V002__init_users_memberships.sql
-- 落地 entities: users (+ tenant_memberships)
-- TypeSpec 来源: tsp/models/user.tsp User/UserStatus, tsp/models/membership.tsp TenantMembership/MembershipStatus
-- 语义说明：
--   * users：tenant-scoped record（TypeSpec User 标注「Always carries tenantId」），一个用户在 N 个租户就有 N 行
--   * tenant_memberships：cross-tenant 视图（一行 / (user_id, tenant_id)），挂载 roleIds[] 与 MembershipStatus
--   * 两表互不冗余：users 承担「此租户视角」的用户档案；tenant_memberships 承担「全局成员关系」的角色+状态

-- 1. 枚举类型
CREATE TYPE user_status AS ENUM (
    'active',
    'invited',
    'suspended',
    'disabled'
);

CREATE TYPE membership_status AS ENUM (
    'active',
    'invited',
    'removed'
);

-- 2. users 表
CREATE TABLE users (
    id            UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id     UUID         NOT NULL,
    username      VARCHAR(64)  NOT NULL,
    email         VARCHAR(255) NOT NULL,
    display_name  VARCHAR(255),
    status        user_status  NOT NULL DEFAULT 'invited',
    password_hash VARCHAR(255),                                  -- bcrypt/argon2 hash；TypeSpec 不暴露此字段（API contract 只走 password 创建时）
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT users_tenant_fk FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,

    -- 同租户内 email/username 唯一
    CONSTRAINT users_tenant_email_unique    UNIQUE (tenant_id, email),
    CONSTRAINT users_tenant_username_unique UNIQUE (tenant_id, username),

    -- email 必为合法格式
    CONSTRAINT users_email_format CHECK (email ~* '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$')
);

CREATE TRIGGER users_set_updated_at
    BEFORE UPDATE ON users
    FOR EACH ROW
    EXECUTE FUNCTION trg_set_updated_at();

-- 3. tenant_memberships 表（cross-tenant 视图）
CREATE TABLE tenant_memberships (
    id          UUID              PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id     UUID              NOT NULL,
    tenant_id   UUID              NOT NULL,
    role_ids    UUID[]            NOT NULL DEFAULT ARRAY[]::UUID[],
    status      membership_status NOT NULL DEFAULT 'invited',
    joined_at   TIMESTAMPTZ       NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT memberships_user_fk   FOREIGN KEY (user_id)
        REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT memberships_tenant_fk FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,

    -- 同一 (user, tenant) 只能有一条 membership；与 users.tenant_id 不冗余是因为 users.id 是 per-tenant 行
    CONSTRAINT memberships_user_tenant_unique UNIQUE (user_id, tenant_id)
);

-- 4. 必要的内联索引
CREATE INDEX idx_users_tenant_id ON users (tenant_id);

COMMENT ON TABLE  users IS               'tenant-scoped 用户档案；TypeSpec User。一行 / (用户, 租户)';
COMMENT ON TABLE  tenant_memberships IS  'cross-tenant 成员关系；TypeSpec TenantMembership。一行 / (user, tenant)';
COMMENT ON COLUMN users.password_hash IS  'bcrypt/argon2 散列；API 不暴露，仅内部校验';
COMMENT ON COLUMN tenant_memberships.role_ids IS 'UUID[] 引用 roles.id；FK 不强制（roles 表在 V003 才建）';