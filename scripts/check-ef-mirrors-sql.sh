#!/usr/bin/env bash
# scripts/check-ef-mirrors-sql.sh — ADR-0010 CI 强制 EF migration DDL 与 shared SQL 字段级 diff=0
#
# 流程：
#   1. 复制 saas-identity-platform-shared/sql/migrations/*.sql 到本仓 migrations/shared.sql（合并）
#   2. dotnet ef migrations script --no-transactions 产 EF 生成的 DDL → migrations/ef.sql
#   3. 规范化两边（strip BEGIN/COMMIT/SET/IF EXISTS/CAST、列 reorder）
#   4. diff；非空 → exit 1
#
# 用法（在 aspnetcore 仓根）：
#   bash scripts/check-ef-mirrors-sql.sh
#
# ADR-0010 规定 EF migration 是 shared SQL 的「强类型镜像」，任何漂移 CI 红。

set -euo pipefail

cd "$(git rev-parse --show-toplevel)"
SHARED_DIR="$(cd ../saas-identity-platform-shared && pwd)"

MIG_DIR="Migrations"
SHARED_SQL="$MIG_DIR/shared.sql"
EF_SQL="$MIG_DIR/ef.sql"

mkdir -p "$MIG_DIR"

# 1. 合并 shared SQL
echo "[check-ef] step 1/3 — concatenating shared/sql/migrations/*.sql → $SHARED_SQL"
cat "$SHARED_DIR/sql/migrations"/V*.sql > "$SHARED_SQL"

# 2. 跑 EF migrations script
echo "[check-ef] step 2/3 — dotnet ef migrations script → $EF_SQL"
dotnet ef migrations script --no-transactions --output "$EF_SQL"

# 3. 规范化两边
normalize() {
  # strip:
  #   - empty lines
  #   - BEGIN/COMMIT transactions
  #   - SET client_min_messages / SET check_function_bodies
  #   - CREATE EXTENSION IF NOT EXISTS
  #   - CREATE OR REPLACE FUNCTION / CREATE TRIGGER (trg_*)
  #   - COMMENT ON (DB-level annotations are nice-to-have, not contract)
  #   - ALTER COLUMN ... TYPE 包装里的 USING ...
  python3 -c '
import sys, re
content = sys.stdin.read()
# split by semicolon; for each stmt, strip whitespace + comments
stmts = []
for raw in content.split(";"):
    lines = []
    for ln in raw.splitlines():
        s = ln.strip()
        if not s: continue
        if s.startswith("--"): continue
        lines.append(s)
    if not lines: continue
    stmt = " ".join(lines).strip()
    if not stmt: continue
    # skip noise
    if re.match(r"BEGIN|COMMIT|START TRANSACTION|ROLLBACK", s, re.I): continue
    if re.match(r"SET\s\s", s, re.I): continue
    if "CREATE EXTENSION" in stmt.upper(): continue
    if "CREATE OR REPLACE FUNCTION" in stmt.upper(): continue
    if "CREATE TRIGGER" in stmt.upper(): continue
    if "COMMENT ON" in stmt.upper(): continue
    # strip column order sensitivity by sorting ALTER TABLE ... ADD COLUMN stmts
    stmts.append(stmt)
# stable order: sort all stmts
stmts.sort()
print("\n;\n".join(stmts))
' < "$1"
}

normalize "$SHARED_SQL" > "$MIG_DIR/shared.norm.sql"
normalize "$EF_SQL"     > "$MIG_DIR/ef.norm.sql"

# 4. diff
echo "[check-ef] step 3/3 — diff"
if diff -u "$MIG_DIR/shared.norm.sql" "$MIG_DIR/ef.norm.sql" > "$MIG_DIR/diff.out"; then
  echo "[check-ef] OK: EF migration mirrors shared SQL"
  rm -f "$MIG_DIR/shared.norm.sql" "$MIG_DIR/ef.norm.sql" "$MIG_DIR/diff.out"
  exit 0
else
  echo "[check-ef] FAIL: EF migration DDL differs from shared SQL. See $MIG_DIR/diff.out"
  cat "$MIG_DIR/diff.out" | head -50
  exit 1
fi