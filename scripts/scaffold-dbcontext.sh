#!/usr/bin/env bash
# scripts/scaffold-dbcontext.sh — dotnet ef dbcontext scaffold 从真库生成 DbContext + entity（DB-First, ADR-0025）
#
# 设计：
# - shared 仓 schema-first（src/db/schema.ts）是 SSOT；drizzle-kit migrate 应用到 DB
# - aspnetcore 仓不手写 DbContext / entity；本脚本跑 EF Core scaffold 从真库重生
# - 输出：src/Infrastructure/Persistence/Generated/AppDbContext.cs + src/Models/Generated/Entity/*.cs
# - 手写 DbContext 业务逻辑（SaveChanges override、partial 类等）通过 partial class 叠加
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
#   0 — scaffold OK
#   1 — dotnet ef 失败 / 产物与 git HEAD drift

set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

DB_PROJECT="src/Saas.Identity.AspNetCore.csproj"
# dotnet ef 的 --output-dir / --context-dir 是相对于 csproj 的；
# csproj 在 src/，所以目标相对路径是 Infrastructure/Persistence/Generated
OUTPUT_DIR="Infrastructure/Persistence/Generated"

# 默认连接（与 scripts/lib/db-env.sh 同套）
PG_HOST="${PG_HOST:-100.79.128.25}"
PG_PORT="${PG_PORT:-5432}"
PG_DATABASE="${PG_DATABASE:-saas_dev}"
PG_USER="${PG_USER:-postgres}"
PG_PASSWORD="${PG_PASSWORD:-}"

CONNECTION="Host=${PG_HOST};Port=${PG_PORT};Database=${PG_DATABASE};Username=${PG_USER};Password=${PG_PASSWORD}"

echo "[scaffold-dbcontext] step 1/2 — dotnet ef dbcontext scaffold"
dotnet ef dbcontext scaffold \
    "$CONNECTION" \
    Npgsql.EntityFrameworkCore.PostgreSQL \
    --project "$DB_PROJECT" \
    --output-dir "$OUTPUT_DIR" \
    --context AppDbContext \
    --context-dir "$OUTPUT_DIR" \
    --force

echo "[scaffold-dbcontext] step 2/2 — git diff src/${OUTPUT_DIR}/"
if ! git diff --exit-code --quiet "src/${OUTPUT_DIR}/" 2>/dev/null; then
  echo "[scaffold-dbcontext] FATAL: scaffold 产物与 git HEAD 不一致" >&2
  echo "[scaffold-dbcontext]        处理：确认 DB 是最新（shared 已 db:migrate），" >&2
  echo "[scaffold-dbcontext]        然后 git add src/${OUTPUT_DIR}/ && git commit" >&2
  exit 1
fi

echo "[scaffold-dbcontext] OK"
echo "[scaffold-dbcontext]    AppDbContext + entity 已与 DB 同步；DB-First sync 绿"
