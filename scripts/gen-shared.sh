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

echo "[gen-shared] step 2/2 — aspnetcore: NSwag → src/Controllers/Generated/ + src/Models/Generated/..."
mkdir -p "$ROOT/src/Controllers/Generated" "$ROOT/src/Models/Generated"

# Run NSwag CLI with the .nswag config. NSwag reads openapi.yaml from
# the path declared in `documentGenerator.fromDocument.url`.
(cd "$ROOT" && nswag run "$NSWAG_CONFIG")

echo "[gen-shared] OK"