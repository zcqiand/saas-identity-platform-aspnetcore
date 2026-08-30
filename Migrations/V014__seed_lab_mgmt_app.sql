-- V014__seed_lab_mgmt_app.sql
-- 落地 lab-mgmt OAuth client（建筑工程实验室管理系统）。
-- 让 3 个 saas 后端（nextjs / aspnetcore / springboot）共用同一 app 记录，
-- 同一固定 UUID 让 lab 仓在任一 IdP 拿到的 sub claim 一致。
--
-- Phase 6 决策：
--   * client_id 用固定 UUID '11111111-1111-1111-1111-111111111111'（不是字符串 'lab-mgmt'）。
--     原因：TypeSpec tsp/routes/oauth.tsp:21,43 给 AuthorizeCodeRequest/TokenRequest 的
--     clientId 加了 @format("uuid")，NSwag codegen 给 saas-aspnetcore 生成 System.Guid、
--     给 saas-springboot 生成 UUID，只有 saas-nextjs 走 string。所以为了让 3 个 saas 后端
--     都能按同一 clientId 查 apps 表，clientId 必须是 UUID 格式。
--     —— 后续 PR 改 TypeSpec 移除 @format("uuid") 后, 可把 client_id 改回 'lab-mgmt'。
--   * client_secret 暂存 plaintext 'lab-mgmt-secret'（与 lab-springboot application.yml dev 默认同款）
--     —— 后续 PR 改 Argon2 hash + 启动时 env 注入；这是技术债 follow-up。
--   * redirect_uris 覆盖 lab 家族全 prod 域名 + dev localhost 5173/5174/3001 callback。
--   * scopes: lab.read, lab.write（与 saas-nextjs/src/seeds/apps.json 同款）。
--   * grant_types: authorization_code + refresh_token（client_credentials 给机器调用，独立 PR）。
--   * ON CONFLICT (client_id) DO NOTHING：保证已有 seed（saas-nextjs/src/seeds/apps.json
--     首次启动写入）不被覆盖 —— saas-nextjs 那份的 clientId 也应是同一个 UUID
--     （saas-identity-platform-nextjs/src/seeds/apps.json 同步改 clientId="11111111-..."）。
--
-- 同 V014 落地的 oauth_codes 表，供 Phase 6 真 OAuth（不再依赖 saas-nextjs 进程内 oauth-store）。

-- 1. lab-mgmt app seed
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

-- 2. oauth_codes 表 — Phase 6 真 OAuth 的 code + refresh_token 存储
-- saas-aspnetcore (AppDbContext) 与 saas-springboot (JPA) 都映射这张表。
-- 镜像 saas-nextjs/src/lib/oauth-store.ts 的字段，扩展 grant_type 列区分 authorization_code / refresh_token。
-- 幂等（IF NOT EXISTS）：saas-springboot flyway 链已有 V009__init_oauth_codes.sql 建同名表
-- （DDL 逐字段相同）。本文件经 gen-shared.sh 拷入 springboot migration 目录后会在 V009
-- 之后执行，不加 IF NOT EXISTS 会撞 "relation already exists" 起崩容器 --
-- 2026-08-26 lab 仓 V014/V015 撞号事故的同款雷，此为拆雷点。
CREATE TABLE IF NOT EXISTS oauth_codes (
    id              UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    code            VARCHAR(255) NOT NULL,                                  -- auth code 或 refresh token (统一存)
    grant_type      VARCHAR(32)  NOT NULL DEFAULT 'authorization_code'
                    CHECK (grant_type IN ('authorization_code', 'refresh_token')),
    app_id          UUID         NOT NULL,
    user_id         UUID,                                                   -- authorization_code 交换时填
    tenant_id       UUID         NOT NULL,
    redirect_uri    VARCHAR(2048),
    scope           VARCHAR(512),
    expires_at      TIMESTAMPTZ  NOT NULL,
    consumed_at     TIMESTAMPTZ,                                            -- 一次性消费标记
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT oauth_codes_code_unique UNIQUE (code),
    CONSTRAINT oauth_codes_app_fk FOREIGN KEY (app_id)
        REFERENCES apps (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_oauth_codes_app_id     ON oauth_codes (app_id);
CREATE INDEX IF NOT EXISTS idx_oauth_codes_expires_at ON oauth_codes (expires_at);
CREATE INDEX IF NOT EXISTS idx_oauth_codes_user_id    ON oauth_codes (user_id);

COMMENT ON TABLE  oauth_codes IS               'OAuth 2.0 authorization_code + refresh_token 存储；Phase 6 替代 saas-nextjs 进程内 oauth-store';
COMMENT ON COLUMN oauth_codes.code IS          'auth code 或 refresh token；前者一次性消费（consumed_at 非 NULL），后者可旋转换发';
COMMENT ON COLUMN oauth_codes.grant_type IS    'authorization_code (TTL 10min) 或 refresh_token (TTL 7d)';
COMMENT ON COLUMN oauth_codes.user_id IS       'authorization_code 创建时为 NULL；/token 交换后填入（saas 侧 user.id）';