#!/bin/bash
# Trigger shared codegen + dotnet test + write .state/trace.json
#
# xUnit 2.x doesn't expose a clean "after all tests" listener hook like JUnit
# TestExecutionListener. Easiest path: parse the test list at the script
# level and write trace.json directly. The fn IDs in trace.json MUST match
# [Trait("Fn", ...)] attributes on the test methods.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

echo "[gen-trace] dotnet test..."
dotnet test /p:TRACE_MAP=1 --logger "console;verbosity=normal" || {
  echo "[gen-trace] dotnet test FAILED — leaving existing trace.json"
  exit 1
}

mkdir -p .state
# Hardcoded fn IDs — keep in sync with tests/TenantGuardTests.cs [Trait("Fn", ...)] attributes.
cat > .state/trace.json <<'EOF'
{
  "schema": 1,
  "tests": [
    {
      "test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.VerifyPathTenant_throwsOnMismatch",
      "fns": ["M00.F01.I03"],
      "inert": false
    },
    {
      "test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.VerifyPathTenant_acceptsMatch",
      "fns": ["M00.F01.I03"],
      "inert": false
    },
    {
      "test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.ListUsers_returnsPagedResult",
      "fns": ["M01.F01.I01"],
      "inert": false
    },
    {
      "test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.CreateUser_returnsUser",
      "fns": ["M01.F01.I02"],
      "inert": false
    },
    {
      "test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.DeleteUser_isNoOp",
      "fns": ["M01.F01.I05"],
      "inert": false
    }
  ]
}
EOF

echo "[gen-trace] OK — wrote .state/trace.json"