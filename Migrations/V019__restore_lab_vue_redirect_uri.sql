-- V019__restore_lab_vue_redirect_uri.sql
-- 修复 V016 把 apps.redirect_uris 里 lab-vue 条目弄丢的事故（2026-09-03 prod SSO 400）。
--
-- 根因（V017 client_id 回退的同款事故模式）：
--   * V014/V009 的 lab-management 行 redirect_uris 含 'https://lab-vue.xiangru.uk/login'；
--   * V016__seed_family_fixtures（2026-08-31）重灌 apps 时列表漏了 lab-vue，
--     与 saas-nextjs/msw src/seeds/apps.json 同源同漏（两份 seed 一并修）。
--   * 后果：lab-vue SSO 跳 saas 登录后 POST /api/v1/oauth/authorize
--     → 400 INVALID_REDIRECT_URI（lab-react 精确匹配 200、lab-vue 400 复现实锤）。
--   * V017 只归一了 client_id，没救回 redirect_uris。
--
-- 修复（幂等，可重跑；V017 同款防御模式）：
--   UPDATE（不用 INSERT+ON CONFLICT）补齐 lab-vue prod 域名条目，
--   并防御性带上 dev 端口段（V018 后 5200 段）的 lab 家族完整列表，
--   不覆盖未知条目（array_cat 去重）。

UPDATE apps
SET redirect_uris = (
    SELECT ARRAY(
        SELECT DISTINCT u
        FROM unnest(array_cat(
            redirect_uris,
            ARRAY[
                'https://lab-vue.xiangru.uk/login',
                'https://lab-react.xiangru.uk/login',
                'https://lab-nextjs.xiangru.uk/login',
                'http://localhost:5200/login',      -- lab-msw（跳板登录页）
                'http://localhost:5201/callback',   -- lab-nextjs SSO callback
                'http://localhost:5202/login',      -- lab-react dev
                'http://localhost:5203/login'       -- lab-vue dev
            ]::TEXT[]
        )) AS u
        ORDER BY u
    )
),
updated_at = CURRENT_TIMESTAMP
WHERE code = 'lab-management'
  AND NOT redirect_uris @> ARRAY['https://lab-vue.xiangru.uk/login'];

-- 验证（事务内跑，lab-management 行缺 lab-vue 即回滚）：
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM apps
        WHERE code = 'lab-management'
          AND status = 'active'
          AND NOT redirect_uris @> ARRAY['https://lab-vue.xiangru.uk/login']
    ) THEN
        RAISE EXCEPTION 'V019: lab-management redirect_uris 仍缺 https://lab-vue.xiangru.uk/login';
    END IF;
END $$;
