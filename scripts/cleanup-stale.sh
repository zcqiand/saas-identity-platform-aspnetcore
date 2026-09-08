#!/usr/bin/env bash
# scripts/cleanup-stale.sh — ADR-0026 §10 V2 半自动孤儿清理
#
# 默认 --dry-run：只列出 SAFE 孤儿，不删
# --apply：实际 `git rm` SAFE 孤儿；UNSAFE 永远不自动删
#
# 用法：
#   bash scripts/cleanup-stale.sh              # dry-run
#   bash scripts/cleanup-stale.sh --apply     # 实际删
#
# 依赖：python3（与 marker 写共用）+ jq（输出友好，但缺 jq 时退化到 python3 解析）
#
# 跨 backend 仓（springboot / aspnetcore）通用，因为 .state/orphans.json
# 里已经标好 SAFE/UNSAFE 分类。本仓放一份，从 templates/cleanup-stale.sh.tmpl 复制即可。

set -euo pipefail

CONSUMER_REPO="$(basename "$(git rev-parse --show-toplevel)")"
ORPHANS_FILE="$(git rev-parse --show-toplevel)/../..state/orphans.json"
# 注：suite 根的 .state/ 是父仓 state；本脚本在 consumer 仓内运行时，路径向上跳 2 级
SUITE_ROOT="$(git rev-parse --show-toplevel)/../.."
ORPHANS_FILE="$SUITE_ROOT/.state/orphans.json"

if [ ! -f "$ORPHANS_FILE" ]; then
  echo "[cleanup-stale] FATAL: $ORPHANS_FILE 不存在" >&2
  echo "[cleanup-stale]        先在 suite 根跑 python scripts/check_align.py 生成" >&2
  exit 1
fi

APPLY=0
[ "${1:-}" = "--apply" ] && APPLY=1

echo "[cleanup-stale] consumer: $CONSUMER_REPO"
echo "[cleanup-stale] orphans: $ORPHANS_FILE"
echo "[cleanup-stale] mode: $([ "$APPLY" = "1" ] && echo APPLY || echo DRY-RUN)"
echo ""

# 提取本 consumer 的 SAFE 孤儿列表（用 python3 避免 jq 依赖）
SAFE_ORPHANS=$(python3 - "$ORPHANS_FILE" "$CONSUMER_REPO" <<'PYEOF'
import json, sys
orphans_path, repo = sys.argv[1], sys.argv[2]
with open(orphans_path, encoding="utf-8") as f:
    data = json.load(f)
# 跨 family 找本 repo
for scope_data in data.values():
    for c in scope_data.get("consumers", []):
        if c["repo"] == repo:
            for o in c.get("orphans", []):
                if o["classification"] == "SAFE":
                    print(o["file"])
            sys.exit(0)
PYEOF
)

if [ -z "$SAFE_ORPHANS" ]; then
  echo "[cleanup-stale] 无 SAFE 孤儿（consumer 已干净）"
  exit 0
fi

echo "[cleanup-stale] 准备 $(echo "$SAFE_ORPHANS" | wc -l) 个 SAFE 孤儿："
echo "$SAFE_ORPHANS" | sed 's/^/  /'
echo ""

if [ "$APPLY" = "0" ]; then
  echo "[cleanup-stale] DRY-RUN：以上文件会被 git rm。Re-run with --apply 实际删除。"
  exit 0
fi

# APPLY 模式
echo "[cleanup-stale] APPLY: git rm 上述文件..."
cd "$(git rev-parse --show-toplevel)"
echo "$SAFE_ORPHANS" | while IFS= read -r f; do
  if [ -n "$f" ] && [ -f "$f" ]; then
    git rm "$f"
    echo "[cleanup-stale]   removed: $f"
  fi
done

echo ""
echo "[cleanup-stale] 完成后："
echo "  1. git commit -m 'chore: ADR-0026 §10 cleanup SAFE orphans'"
echo "  2. 重跑 python scripts/check_align.py --matrix 验证"
echo "  3. 跑本仓 L4 测试确认无回归"
