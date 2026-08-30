-- V005__init_oauth_apps_menus.sql
-- 落地 entities: apps, menus, role_menu_grants
-- TypeSpec 来源: tsp/models/app.tsp App/AppStatus/OAuthGrantType, tsp/models/menu.tsp Menu/MenuType/MenuStatus,
--                 tsp/models/role-menu-grant.tsp RoleMenuGrant
-- 语义：
--   * apps：平台级统一实体（同时承担菜单承载 + OAuth client）；code 全平台唯一
--   * menus：挂载在某个 app 下；parent_id 自引用树形结构；type 区分 group/page/action
--   * role_menu_grants：tenant-scoped M:N（role → menus）；整批替换语义（PUT setRoleMenus）
--   * 一个 app 一份记录；同时具备展示字段（icon/sortOrder/description）与 OAuth 字段
--     （clientId/clientSecret/redirectUris/scopes/grantTypes/isFirstParty）

CREATE TYPE app_status AS ENUM (
    'active',
    'disabled'
);

CREATE TYPE oauth_grant_type AS ENUM (
    'authorization_code',
    'refresh_token',
    'client_credentials',
    'password'
);

CREATE TYPE menu_type AS ENUM (
    'group',
    'page',
    'action'
);

CREATE TYPE menu_status AS ENUM (
    'active',
    'disabled'
);

-- 1. apps 表（平台级；不挂 tenant）
CREATE TABLE apps (
    id                 UUID            PRIMARY KEY DEFAULT uuid_generate_v4(),
    code               VARCHAR(64)     NOT NULL,                          -- 平台内唯一；菜单承载用
    name               VARCHAR(255)    NOT NULL,
    description        TEXT,
    icon               VARCHAR(64),
    sort_order         INTEGER         NOT NULL DEFAULT 0,
    status             app_status      NOT NULL DEFAULT 'active',

    -- OAuth 集成字段
    client_id          VARCHAR(128)    NOT NULL,                          -- OAuth client_id
    client_secret_hash VARCHAR(255),                                      -- 散列；明文不在数据库里
    redirect_uris      TEXT[]          NOT NULL DEFAULT ARRAY[]::TEXT[],
    scopes             TEXT[]          NOT NULL DEFAULT ARRAY[]::TEXT[],
    grant_types        oauth_grant_type[] NOT NULL DEFAULT ARRAY[]::oauth_grant_type[],
    is_first_party     BOOLEAN         NOT NULL DEFAULT FALSE,

    created_at         TIMESTAMPTZ     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at         TIMESTAMPTZ     NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT apps_code_unique      UNIQUE (code),
    CONSTRAINT apps_client_id_unique UNIQUE (client_id)
);

CREATE TRIGGER apps_set_updated_at
    BEFORE UPDATE ON apps
    FOR EACH ROW
    EXECUTE FUNCTION trg_set_updated_at();

-- 2. menus 表（树形；挂载在 app 下）
CREATE TABLE menus (
    id          UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    app_id      UUID         NOT NULL,
    parent_id   UUID,                                                   -- 自引用；NULL 表示根
    code        VARCHAR(64)  NOT NULL,
    name        VARCHAR(255) NOT NULL,
    path        VARCHAR(512),
    icon        VARCHAR(64),
    type        menu_type    NOT NULL DEFAULT 'page',
    sort_order  INTEGER      NOT NULL DEFAULT 0,
    status      menu_status  NOT NULL DEFAULT 'active',
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT menus_app_fk FOREIGN KEY (app_id)
        REFERENCES apps (id) ON DELETE CASCADE,
    CONSTRAINT menus_parent_fk FOREIGN KEY (parent_id)
        REFERENCES menus (id) ON DELETE CASCADE,

    -- 同 app 内 code 唯一（树形下父子可同名）
    CONSTRAINT menus_app_code_unique UNIQUE (app_id, code)
    -- NOTE: 「父菜单同 app」一致性原用 CHECK 子查询，PG 不允许（CHECK 不能含子查询），
    -- 已移除；该完整性由应用层（Menu Service）保证。需 DB 级强制时可改 BEFORE INSERT/UPDATE 触发器。
);

CREATE TRIGGER menus_set_updated_at
    BEFORE UPDATE ON menus
    FOR EACH ROW
    EXECUTE FUNCTION trg_set_updated_at();

-- 3. role_menu_grants 表（tenant-scoped M:N；整批替换语义）
CREATE TABLE role_menu_grants (
    role_id    UUID        NOT NULL,
    tenant_id  UUID        NOT NULL,
    menu_ids   UUID[]      NOT NULL DEFAULT ARRAY[]::UUID[],
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    PRIMARY KEY (role_id),
    CONSTRAINT rmg_role_fk   FOREIGN KEY (role_id)
        REFERENCES roles (id) ON DELETE CASCADE,
    CONSTRAINT rmg_tenant_fk FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE
    -- NOTE: 「role 属于 tenant_id」一致性原用 CHECK 子查询，PG 不允许，已移除；
    -- 应用层 TenantRoleMenuService 已校验 role 存在性。需 DB 级强制时可改触发器。
);

-- 4. 内联索引
CREATE INDEX idx_menus_app_id        ON menus (app_id);
CREATE INDEX idx_menus_parent_id     ON menus (parent_id);
CREATE INDEX idx_role_menu_grants_tenant_id ON role_menu_grants (tenant_id);

COMMENT ON TABLE  apps IS              '平台级统一实体：菜单承载 + OAuth client；TypeSpec App';
COMMENT ON TABLE  menus IS             '树形菜单节点；parent_id 自引用；TypeSpec Menu';
COMMENT ON TABLE  role_menu_grants IS  'tenant-scoped 角色↔菜单 M:N；TypeSpec RoleMenuGrant；整批 PUT';
COMMENT ON COLUMN apps.client_secret_hash IS 'OAuth client_secret 散列；明文不出库';