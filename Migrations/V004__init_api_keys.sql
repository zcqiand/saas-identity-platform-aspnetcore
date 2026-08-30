-- V004__init_api_keys.sql
-- 落地 entities: api_keys
-- TypeSpec 来源: tsp/models/api-key.tsp ApiKey / ApiKeyStatus
-- 字段语义补充：
--   * prefix：明文前 8-16 字符（用于 UI 列表展示「哪个 key」）；secret 完整散列存 secret_hash
--   * secret_hash：argon2/bcrypt 散列；CreateApiKeyResponse.secret 仅创建时返回一次（API contract）
--   * scopes：字符串数组（resource:action 形式），与 permissions.code 字典对齐

CREATE TYPE api_key_status AS ENUM (
    'active',
    'revoked',
    'expired'
);

CREATE TABLE api_keys (
    id           UUID            PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id    UUID            NOT NULL,
    name         VARCHAR(128)    NOT NULL,
    prefix       VARCHAR(16)     NOT NULL,                                   -- 明文前缀
    secret_hash  VARCHAR(255)    NOT NULL,                                   -- 散列；明文只在创建响应里返回一次
    status       api_key_status  NOT NULL DEFAULT 'active',
    scopes       TEXT[]          NOT NULL DEFAULT ARRAY[]::TEXT[],
    created_at   TIMESTAMPTZ     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_used_at TIMESTAMPTZ,
    expires_at   TIMESTAMPTZ,
    revoked_at   TIMESTAMPTZ,

    CONSTRAINT api_keys_tenant_fk FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,

    -- prefix 在同租户内唯一（同一前缀不能发两次，避免误把旧 key 当新 key）
    CONSTRAINT api_keys_tenant_prefix_unique UNIQUE (tenant_id, prefix),

    -- status 与 revoked_at / expires_at 一致性
    CONSTRAINT api_keys_revoked_at_consistency CHECK (
        (status = 'revoked' AND revoked_at IS NOT NULL)
        OR (status <> 'revoked')
    )
);

-- 内联索引
CREATE INDEX idx_api_keys_tenant_id    ON api_keys (tenant_id);
CREATE INDEX idx_api_keys_status       ON api_keys (status);
CREATE INDEX idx_api_keys_expires_at   ON api_keys (expires_at);

COMMENT ON TABLE  api_keys IS              'tenant-scoped API key；TypeSpec ApiKey。secret_hash 不可逆散列';
COMMENT ON COLUMN api_keys.prefix IS       '明文前缀（8-16 字符），用于 UI 列表展示「哪个 key」';
COMMENT ON COLUMN api_keys.secret_hash IS  'argon2/bcrypt 散列；明文 secret 仅在 CreateApiKeyResponse 一次性返回';