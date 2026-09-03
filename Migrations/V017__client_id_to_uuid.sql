-- V017__client_id_to_uuid.sql
-- 修复 V016 把 apps.client_id 回退成字符串的事故（2026-08-31 contract-test I26 发现）。
--
-- 背景（V014 Phase 6 决策，V016 违反）：
--   * TypeSpec oauth.tsp/auth.tsp 给 clientId 加了 @format("uuid")：
--     NSwag 给 saas-aspnetcore 生成 System.Guid、给 saas-springboot 生成 UUID、
--     saas-nextjs 走 string。3 个 saas 后端按同一 clientId 查 apps 表，
--     client_id 必须是 UUID 格式。
--   * V014/V009 正确灌了 '11111111-1111-1111-1111-111111111111'；
--     V016 又灌 'lab-mgmt'/'erp'/'crm' 字符串。V016 晚于 V014 执行，
--     库里最终是字符串行（V009 注释同款事故模式：「ON CONFLICT + 固定 id」
--     对存量库不是归一而是并存/回退）。
--   * 后果：saas-nextjs/msw（string 查找）能跑，saas-springboot/aspnetcore
--     （UUID codegen + findByClientId）永远 401/500 —— 契约测试 M96.F02.I26 抓获。
--
-- 修复（幂等 UPDATE，不用 INSERT+ON CONFLICT）：
--   client_id 列是 VARCHAR(128)（V005 DDL），存 UUID 文本即可 ——
--   client_id::uuid 归一为 app.id 的文本形式（=client_id=app.id，V014 语义）。
--   存量 oauth_codes 无 client_id 列（经 app_id 关联），不受影响。

UPDATE apps SET client_id = id::text, updated_at = CURRENT_TIMESTAMP
WHERE client_id::text <> id::text;

-- 验证（事务内跑，不一致即回滚）：
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM apps WHERE client_id::text <> id::text) THEN
        RAISE EXCEPTION 'V017: apps.client_id 归一失败，仍有 client_id <> id 的行';
    END IF;
END $$;
