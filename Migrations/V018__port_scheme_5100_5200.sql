-- V018__port_scheme_5100_5200.sql
-- 2026-09-02 端口全局重规划：saas=5100 段、lab=5200 段（conventions §6 新 SSOT）。
-- apps 表 redirect_uris 里的 dev localhost URI 同步换到新端口。
-- 见 docs/conventions/multi-repo-family.md §6 与计划 functional-rolling-snail。
--
-- 端口映射（dev only，prod 域名行不动）：
--   lab:  msw 5173→5200 | nextjs 3001→5201 | react 5173→5202 | vue 5173→5203
--   saas: msw 5174→5100 | nextjs 3000→5101 | react 5173→5102 | vue 5175→5103
--
-- 幂等 UPDATE（V017 同款防御模式）：逐 URI 精确替换，不整列覆盖，
-- 不碰 xiangru.uk 行；已应用过本迁移的库再跑一遍 = no-op。

UPDATE apps
SET redirect_uris = (
    SELECT ARRAY(
        SELECT CASE
            -- lab 家族 dev URI（V009/V014/V016 种下的旧端口）
            WHEN u = 'http://localhost:5173/login'    THEN 'http://localhost:5202/login'   -- lab react/vue dev
            WHEN u = 'http://localhost:5174/login'    THEN 'http://localhost:5200/login'   -- lab-msw（跳板登录页）
            WHEN u = 'http://localhost:3001/callback' THEN 'http://localhost:5201/callback' -- lab-nextjs SSO callback
            -- saas 家族 dev URI（未来 saas 前端接入时由各自种子里的新端口覆盖，此处防御性归一）
            WHEN u = 'http://localhost:5175/login'    THEN 'http://localhost:5103/login'   -- saas-vue dev
            ELSE u
        END
        FROM unnest(redirect_uris) AS u
    )
),
updated_at = CURRENT_TIMESTAMP
WHERE redirect_uris && ARRAY[
    'http://localhost:5173/login',
    'http://localhost:5174/login',
    'http://localhost:3001/callback',
    'http://localhost:5175/login'
];

-- 验证（事务内跑，残留旧端口 URI 即回滚）：
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM apps, unnest(redirect_uris) AS u
        WHERE u LIKE 'http://localhost:5173/%'
           OR u LIKE 'http://localhost:5174/%'
           OR u LIKE 'http://localhost:3001/%'
           OR u LIKE 'http://localhost:5175/%'
    ) THEN
        RAISE EXCEPTION 'V018: apps.redirect_uris 仍有旧端口 dev URI，归一未完成';
    END IF;
END $$;
