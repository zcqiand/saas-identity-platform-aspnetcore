#!/bin/bash
# Generate ASP.NET Core Controllers + DTOs from shared's OpenAPI.yaml.
#
# Architecture (ADR-0007 + ADR-0025):
# - shared 仓是 schema-first 双 SSOT 仓（TypeSpec → OpenAPI.yaml + Drizzle TS → SQL）
# - 本仓走 DB-First：DbContext + entity 由 dotnet ef dbcontext scaffold 从真库重生（见 scripts/scaffold-dbcontext.sh）
# - 不再跑 EF Migrations；schema 由 shared 仓 drizzle-kit migrate 统一管理
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

echo "[gen-shared] step 2/2 — aspnetcore: NSwag → src/Controllers/Generated/ + src/Models/Generated/..."
mkdir -p "$ROOT/src/Controllers/Generated" "$ROOT/src/Models/Generated"

# Run NSwag CLI with the .nswag config. NSwag reads openapi.yaml from
# the path declared in `documentGenerator.fromDocument.url`.
(cd "$ROOT" && nswag run "$NSWAG_CONFIG")

# Post-process NSwag output: inject [JsonIgnore(WhenWritingDefault)] above
# specific value-type fields whose TypeSpec uses '?' (optional) but the
# TypeSpec → OpenAPI 3.1 emit doesn't carry a nullable union. NSwag then
# generates non-nullable Guid / DateTimeOffset, and the implementation
# encodes "missing value" as default (Guid.Empty / DateTimeOffset.MinValue)
# via `?? Guid.Empty` / `?? default`. Without this attribute, System.Text.Json
# would serialize Guid.Empty as "00000000-..." or MinValue as "0001-01-01...",
# which contract-test normalize() can't reconcile with msw/nextjs/springboot's
# null/missing output. The attribute suppresses default-value emission so the
# JSON simply omits the field — which normalize() treats as identical to null
# (M96.F01.I03: 「字段缺失」与「显式 null」等价).
#
# Keep this list aligned with [lab-management-systems-shared-aspnetcore-co-rule]
# in the contract-test ADR; when a new endpoint emits a default-encoded field,
# add it here.
CONTROLLERS="$ROOT/src/Controllers/Generated/Controllers.cs"
# ADR-0032 (2026-09-12)：currentTenantId（LoginResponse / CurrentUser，optional uuid）加入
# 默认值抑制清单 —— 实现以 Guid.Empty 表示「无当前租户」，不抑制会序列化出
# "00000000-0000-0000-0000-000000000000"，与 msw/nextjs/springboot 的字段缺失不等价。
for FIELD in parentId lastUsedAt expiresAt revokedAt currentTenantId; do
  # Match the JsonPropertyName attribute line and inject JsonIgnore above it.
  # Use Python (not sed) so the multi-platform shell handles newline insertion reliably.
  python3 - "$CONTROLLERS" "$FIELD" <<'PY'
import re, sys, pathlib
path, field = sys.argv[1], sys.argv[2]
p = pathlib.Path(path)
src = p.read_text(encoding="utf-8")
attr = ('[System.Text.Json.Serialization.JsonIgnore(Condition = '
        'System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]')
pattern = re.compile(
    r'^(\s*)\[System\.Text\.Json\.Serialization\.JsonPropertyName\("' + re.escape(field) + r'"\)\]',
    re.MULTILINE,
)
new = pattern.sub(r'\1' + attr + r'\n\1[System.Text.Json.Serialization.JsonPropertyName("' + field + r'")]', src)
if new == src:
    print(f'[gen-shared] WARN: {field} not found in {path}', file=sys.stderr)
else:
    p.write_text(new, encoding="utf-8")
    print(f'[gen-shared] patched [JsonIgnore] onto {field}')
PY
done

# ADR-0027 §4: NSwag 产 `Controllers.cs` 单文件 → orphan check 视 stem `Controllers`
# 为不在 shared 的 namespace → SAFE orphan。改名 `OAuth.cs`（stem `OAuth` ∈ shared
# 期望集合），同 namespace 内的所有 abstract class + DTO 仍可编译。下一轮 gen-shared
# 会重新生成同名 `Controllers.cs`，此处 rename 是稳定做法（post-process step 每次跑）。
if [ -f "$CONTROLLERS" ]; then
  mv "$CONTROLLERS" "$ROOT/src/Controllers/Generated/OAuth.cs"
  echo "[gen-shared] renamed Controllers.cs → OAuth.cs (ADR-0027 §4 orphan fix)"
fi

echo "[gen-shared] OK"
echo "[gen-shared]    DB schema 同步请跑: bash scripts/scaffold-dbcontext.sh（shared 已 db:migrate 之后）"

# ADR-0026 §2: 写 last-gen-shared.json marker，供 suite 跨仓 staleness check 使用。
# 失败不阻塞 gen-shared.sh —— staleness 是 warning（V1 档）不是 build blocker。
SHARED_SHA=$(cd "$SHARED_DIR" && git rev-parse HEAD)
MARKER="$ROOT/.state/last-gen-shared.json"
mkdir -p "$ROOT/.state"

if python3 - "$MARKER" "$SHARED_SHA" "$(basename "$0")" "$(basename "$ROOT")" <<'PYEOF'
import datetime, json, sys

marker_path, shared_sha, cmd, repo = sys.argv[1:5]
try:
    with open(marker_path, encoding="utf-8") as f:
        marker = json.load(f)
except (FileNotFoundError, json.JSONDecodeError):
    marker = {}

now = datetime.datetime.now(datetime.timezone.utc).isoformat()
if cmd.startswith("gen-shared"):
    marker["api_synced_sha"] = shared_sha
    marker["api_synced_at"] = now
    marker["api_synced_cmd"] = cmd
elif cmd.startswith("scaffold"):
    marker["db_synced_sha"] = shared_sha
    marker["db_synced_at"] = now
    marker["db_synced_cmd"] = cmd

shas = [s for s in (marker.get("api_synced_sha"), marker.get("db_synced_sha")) if s]
marker["shared_sha"] = max(shas) if shas else shared_sha
marker["consumer_repo"] = repo

with open(marker_path, "w", encoding="utf-8") as f:
    json.dump(marker, f, ensure_ascii=False, indent=2)
    f.write("\n")
PYEOF
then
  echo "[gen-shared]    ADR-0026 marker 已落盘: $MARKER (shared HEAD ${SHARED_SHA:0:7})"
else
  echo "[gen-shared]    WARN: marker 写失败（python3 缺失？权限？）—— staleness 将报 UNKNOWN" >&2
fi