-- V003__init_roles_permissions.sql
-- 落地 entities: roles, permissions, role_permissions (junction)
-- TypeSpec 来源: tsp/models/role.tsp Role（permissionIds: string[] 不透明 → 拆出 permissions + role_permissions 实体）
-- 语义：
--   * permissions：平台级 permission 字典（resource:action 形式 code，如 "users:read"）
--   * roles：tenant-scoped，一行 / 租户；code 在同租户内唯一
--   * role_permissions：M:N 关系表；FK 级联删除

-- 1. roles 表
CREATE TABLE roles (
    id            UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id     UUID         NOT NULL,
    code          VARCHAR(64)  NOT NULL,
    name          VARCHAR(255) NOT NULL,
    description   TEXT,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT roles_tenant_fk FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,

    -- 同租户内 role code 唯一
    CONSTRAINT roles_tenant_code_unique UNIQUE (tenant_id, code)
);

CREATE TRIGGER roles_set_updated_at
    BEFORE UPDATE ON roles
    FOR EACH ROW
    EXECUTE FUNCTION trg_set_updated_at();

-- 2. permissions 表（平台级；不属于任何租户）
CREATE TABLE permissions (
    id          UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    code        VARCHAR(128) NOT NULL,                       -- 形如 "users:read" / "audit:export"
    name        VARCHAR(255) NOT NULL,
    description TEXT,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT permissions_code_unique UNIQUE (code)
);

-- 3. role_permissions M:N 关系表
CREATE TABLE role_permissions (
    role_id        UUID NOT NULL,
    permission_id  UUID NOT NULL,
    granted_at     TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    PRIMARY KEY (role_id, permission_id),
    CONSTRAINT role_permissions_role_fk FOREIGN KEY (role_id)
        REFERENCES roles (id) ON DELETE CASCADE,
    CONSTRAINT role_permissions_permission_fk FOREIGN KEY (permission_id)
        REFERENCES permissions (id) ON DELETE CASCADE
);

-- 4. 内联索引
CREATE INDEX idx_roles_tenant_id ON roles (tenant_id);
CREATE INDEX idx_role_permissions_role_id       ON role_permissions (role_id);
CREATE INDEX idx_role_permissions_permission_id ON role_permissions (permission_id);

COMMENT ON TABLE  roles IS          'tenant-scoped role；TypeSpec Role；同一 code 在同 tenant 内唯一';
COMMENT ON TABLE  permissions IS    '平台级 permission 字典；TypeSpec 不直接定义，由 roleIds: string[] 推导出';
COMMENT ON TABLE  role_permissions IS 'M:N 关系表；删除 role / permission 级联清理';