#!/usr/bin/env bash
# scripts/scaffold-dbcontext.sh — dotnet ef dbcontext scaffold 从真库生成 DbContext + entity（DB-First, ADR-0025）
#
# 设计：
# - shared 仓 schema-first（src/db/schema.ts）是 SSOT；drizzle-kit migrate 应用到 DB
# - aspnetcore 仓不手写 DbContext / entity；本脚本跑 EF Core scaffold 从真库重生
# - 输出：src/Infrastructure/Persistence/Generated/AppDbContext.cs + src/Models/Generated/Entity/*.cs
# - 手写 DbContext 业务逻辑（SaveChanges override、partial 类等）通过 partial class 叠加
# - 手写 partial 在 src/Infrastructure/Persistence/AppDbContext.cs（不在 Generated/），不会被本脚本动
#
# DB-First 不变量：DB 真值是唯一真源，仓 entity 必须 1:1 镜像。scaffold 的产物应当
# 在 commit 前与 DB 完全一致——orphan entity（DB 已 DROP 但仓还残留的 .cs）必须
# 删掉，不能靠 scaffold --force 覆盖同名（它只覆盖，不删孤儿）。
#
# 用法：
#   bash scripts/scaffold-dbcontext.sh                       # default saas_dev @ 100.79.128.25
#   DATABASE_URL=postgresql://... bash scripts/scaffold-dbcontext.sh
#
# 前提：
# - shared 仓已 `npm run db:migrate`（目标 DB schema 与 src/db/schema.ts 一致）
# - dotnet ef 已安装（dotnet tool install --global dotnet-ef）
# - appsettings.Development.json 或 env DATABASE_URL 提供连接
#
# 退出码：
#   0 — scaffold OK（有变更属正常，下一步 git add/commit）
#   1 — dotnet ef 失败 / PG 连不上

set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

DB_PROJECT="src/Saas.Identity.AspNetCore.csproj"
# dotnet ef 的 --output-dir / --context-dir 是相对于 csproj 的；
# csproj 在 src/，所以目标相对路径是 Infrastructure/Persistence/Generated
OUTPUT_DIR="Infrastructure/Persistence/Generated"
GENERATED_DIR="src/${OUTPUT_DIR}"

# 默认连接（与 scripts/lib/db-env.sh 同套）
PG_HOST="${PG_HOST:-100.79.128.25}"
PG_PORT="${PG_PORT:-5432}"
PG_DATABASE="${PG_DATABASE:-saas_dev}"
PG_USER="${PG_USER:-postgres}"
PG_PASSWORD="${PG_PASSWORD:-}"

CONNECTION="Host=${PG_HOST};Port=${PG_PORT};Database=${PG_DATABASE};Username=${PG_USER};Password=${PG_PASSWORD}"

# step 1/3 — 清空 Generated/（DB-First 不变量：DB 是唯一真源，仓 entity 必须 1:1 镜像）
# 全清含 AppDbContext.cs——scaffold 从 DB 重新生成。手写 partial 在 sibling
# src/Infrastructure/Persistence/AppDbContext.cs（不在 Generated/），不会被本步动。
# 关键：scaffold 必须先全清才能跑——AppDbContext.cs 保留会触发 build 失败（它
# 引用刚被删的 entity 类型），scaffold 无法 --force 覆盖。鸡生蛋问题。
if [ -d "$GENERATED_DIR" ]; then
  echo "[scaffold-dbcontext] step 1/3 — 清空 Generated/（含 AppDbContext.cs）"
  find "$GENERATED_DIR" -maxdepth 1 -type f -name "*.cs" -print -delete
else
  mkdir -p "$GENERATED_DIR"
  echo "[scaffold-dbcontext] step 1/3 — 创建 ${GENERATED_DIR}/"
fi

echo "[scaffold-dbcontext] step 2/3 — dotnet ef dbcontext scaffold"
dotnet ef dbcontext scaffold \
    "$CONNECTION" \
    Npgsql.EntityFrameworkCore.PostgreSQL \
    --project "$DB_PROJECT" \
    --output-dir "$OUTPUT_DIR" \
    --context AppDbContext \
    --context-dir "$OUTPUT_DIR" \
    --force

echo "[scaffold-dbcontext] step 3/3 — git diff src/${OUTPUT_DIR}/（DB-First 真源对照）"
if git diff --exit-code --quiet "src/${OUTPUT_DIR}/" 2>/dev/null; then
  echo "[scaffold-dbcontext] OK  无 diff（DB 与仓 entity 完全一致）"
else
  echo "[scaffold-dbcontext] OK  有 diff — git add src/${OUTPUT_DIR}/ && git commit 落仓"
  git diff --stat "src/${OUTPUT_DIR}/" || true
fi
