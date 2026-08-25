#!/usr/bin/env python
"""Run dotnet test + write .state/trace.json.

xUnit 2.x doesn't expose a clean "after all tests" listener hook like JUnit
TestExecutionListener. Easiest path: emit trace.json with hardcoded fn IDs
that match [Trait("Fn", ...)] attributes on test methods.
"""
import os
import subprocess
import sys
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TESTS_DIR = ROOT / "tests"

print("[gen-trace] dotnet test...", flush=True)
result = subprocess.run(
    ["dotnet", "test", "/p:TRACE_MAP=1", "--nologo"],
    cwd=str(TESTS_DIR),
    capture_output=True,
    text=True,
    encoding="utf-8",
    errors="replace",
)
if result.returncode != 0:
    print(result.stdout[-2000:])
    print(result.stderr[-2000:])
    sys.exit(1)

# Hardcoded fn IDs — keep in sync with tests/*.cs [Trait("Fn",...)] attributes.
trace = {
    "schema": 1,
    "tests": [
        {"test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.VerifyPathTenant_throwsOnMismatch", "fns": ["M00.F01.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.VerifyPathTenant_acceptsMatch",   "fns": ["M00.F01.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.ListUsers_returnsPagedResult",     "fns": ["M01.F01.I01"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.CreateUser_returnsUser",          "fns": ["M01.F01.I02"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.DeleteUser_isNoOp",              "fns": ["M01.F01.I05"], "inert": False},

        # v0.2.0 Phase 6 OAuth (9 个单元测试, EF Core InMemory provider)
        {"test": "Saas.Identity.AspNetCore.Tests.Controllers.OauthControllerTests.Authorize_happyPath_returnsCode", "fns": ["M04.F03.I07"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Controllers.OauthControllerTests.Authorize_invalidClient_throwsUnauthorized", "fns": ["M04.F03.I07"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Controllers.OauthControllerTests.Authorize_invalidRedirectUri_throws", "fns": ["M04.F03.I07"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Controllers.OauthControllerTests.Authorize_invalidScope_throws", "fns": ["M04.F03.I07"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Controllers.OauthControllerTests.Token_authorizationCode_happyPath", "fns": ["M04.F03.I08"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Controllers.OauthControllerTests.Token_alreadyConsumedCode_throws", "fns": ["M04.F03.I08"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Controllers.OauthControllerTests.Token_redirectUriMismatch_throws", "fns": ["M04.F03.I08"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Controllers.OauthControllerTests.Token_refreshToken_happyPath", "fns": ["M04.F03.I09"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Controllers.OauthControllerTests.Token_refreshTokenReuse_throws", "fns": ["M04.F03.I09"], "inert": False},
    ],
}

state_dir = ROOT / ".state"
state_dir.mkdir(exist_ok=True)
trace_path = state_dir / "trace.json"
trace_path.write_text(json.dumps(trace, indent=2) + "\n", encoding="utf-8")
print(f"[gen-trace] wrote {trace_path}", flush=True)
sys.exit(0)