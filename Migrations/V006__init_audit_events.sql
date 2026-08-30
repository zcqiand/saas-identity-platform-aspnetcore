-- V006__init_audit_events.sql
-- 落地 entities: audit_events, audit_retention_policies
-- TypeSpec 来源: tsp/models/audit-event.tsp AuditEvent/AuditAction, tsp/routes/tenant-audit.tsp F02 retention
-- 语义：
--   * audit_events：tenant-scoped 不可变事件记录；insert-only（应用层不 UPDATE/DELETE）
--   * audit_retention_policies：一租户一行；retention_days 决定 auto-purge 时长（M06.F02）
--   * metadata：JSONB 任意键值；运行时由 mapper 强类型化

CREATE TYPE audit_action AS ENUM (
    'user_created',
    'user_updated',
    'user_deleted',
    'role_assigned',
    'role_revoked',
    'login_success',
    'login_failed',
    'oauth_token_issued',
    'api_key_created',
    'api_key_revoked'
);

CREATE TABLE audit_events (
    id              UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id       UUID         NOT NULL,
    actor_user_id   UUID,                                              -- 可空：系统动作无 actor
    action          audit_action NOT NULL,
    target_user_id  UUID,                                              -- 可空：非用户对象
    metadata        JSONB        NOT NULL DEFAULT '{}'::jsonb,
    occurred_at     TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT audit_events_tenant_fk FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,
    CONSTRAINT audit_events_actor_fk FOREIGN KEY (actor_user_id)
        REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT audit_events_target_fk FOREIGN KEY (target_user_id)
        REFERENCES users (id) ON DELETE SET NULL,

    -- metadata 必为 JSON object（不允许顶层数组/字符串）
    CONSTRAINT audit_events_metadata_is_object CHECK (
        metadata IS NOT NULL AND jsonb_typeof(metadata) = 'object'
    )
);

CREATE TABLE audit_retention_policies (
    tenant_id      UUID        PRIMARY KEY,
    retention_days INTEGER     NOT NULL DEFAULT 90,
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT audit_retention_tenant_fk FOREIGN KEY (tenant_id)
        REFERENCES tenants (id) ON DELETE CASCADE,

    CONSTRAINT audit_retention_days_positive CHECK (retention_days >= 1 AND retention_days <= 3650)
);

COMMENT ON TABLE  audit_events IS              'tenant-scoped 不可变审计事件；应用层 insert-only';
COMMENT ON TABLE  audit_retention_policies IS  '一租户一行；决定 audit_events 自动清理窗口（M06.F02）';
COMMENT ON COLUMN audit_events.metadata IS        'JSONB 任意键值；mapper 层做 typed parse';
COMMENT ON COLUMN audit_events.actor_user_id IS 'NULL 表示系统动作（cron / SSO callback）';