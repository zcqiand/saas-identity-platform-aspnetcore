#!/bin/bash
# Generate ASP.NET Core Controllers + DTOs from shared's OpenAPI.yaml.
#
# Architecture change (v0.2.0): shared 仓 is now a pure contract source
# (TypeSpec → OpenAPI.yaml only). Language-specific clients are generated
# per consuming project. This script invokes NSwag CLI instead of copying
# pre-generated C# sources from shared/generated/csharp/ (which no longer
# exist after shared 瘦身).
#
# Generated files (declared in aspnetcore.nswag):
#   - src/Controllers/Generated/<Tag>Controller.cs  — abstract base class
#     with method stubs throwing NotImplementedException
#   - src/Models/Generated/<Name>.cs — DTO records/types
#
# User-implemented controllers live in src/Controllers/Implementation/<Tag>Controller.cs
# as partial classes inheriting the generated base, providing business logic
# (calling TenantGuard.VerifyPathTenant for tenant-scoped endpoints).
set -euo pipefail

SHARED_DIR="$(cd "$(dirname "$0")/../../saas-identity-platform-shared" && pwd)"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OPENAPI="$SHARED_DIR/generated/openapi/openapi.yaml"
NSWAG_CONFIG="$ROOT/aspnetcore.nswag"

echo "[gen-shared] step 1/2 — shared: emit OpenAPI.yaml..."
(cd "$SHARED_DIR" && npm run emit:openapi)

if [ ! -f "$OPENAPI" ]; then
  echo "[gen-shared] ERROR: missing $OPENAPI" >&2
  exit 1
fi

echo "[gen-shared] step 2/3 — aspnetcore: NSwag → src/Controllers/Generated/ + src/Models/Generated/..."
mkdir -p "$ROOT/src/Controllers/Generated" "$ROOT/src/Models/Generated"

# Run NSwag CLI with the .nswag config. NSwag reads openapi.yaml from
# the path declared in `documentGenerator.fromDocument.url`.
(cd "$ROOT" && nswag run "$NSWAG_CONFIG")

# M10.Database (ADR-0007 + ADR-0010) — DB SQL SSOT 落地 + EF Migrations 镜像
echo "[gen-shared] step 3/3 — DB: copy shared/sql/migrations/* + 触发 EF migrations script..."
SHARED_SQL="$SHARED_DIR/sql/migrations"
if [ -d "$SHARED_SQL" ]; then
  mkdir -p "$ROOT/Migrations"
  for f in "$SHARED_SQL"/V*.sql; do
    [ -e "$f" ] || continue
    cp "$f" "$ROOT/Migrations/"
  done
  [ -f "$SHARED_SQL/README.md" ] && cp "$SHARED_SQL/README.md" "$ROOT/Migrations/README.md"

  # 触发 EF migrations script；保证每次 EF Model 变更都重新镜像 shared SQL
  if [ -d "$ROOT/Migrations" ] && [ -n "$(ls -A "$ROOT/Migrations"/*.cs 2>/dev/null)" ]; then
    echo "[gen-shared]     EF migrations 已存在（Migrations/*.cs）；用 scripts/check-ef-mirrors-sql.sh 校验 diff"
    bash "$ROOT/scripts/check-ef-mirrors-sql.sh" || {
      echo "[gen-shared] WARN: EF ↔ SQL diff 不为空；Phase 5 CI 应红"
    }
  else
    echo "[gen-shared]     首次落地；跑: dotnet ef migrations add InitialSchema --project src"
    echo "[gen-shared]     然后 git commit migrations/<timestamp>_InitialSchema.cs"
  fi
else
  echo "[gen-shared] WARN: $SHARED_SQL not found; DB layer skipped"
fi

echo "[gen-shared] OK"