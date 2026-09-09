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
        # M00.F01.I03 — TenantGuard (tests/TenantGuardTests.cs，9/7 重构后保留的 2 个核心测试)
        {"test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.VerifyPathTenant_throwsOnMismatch", "fns": ["M00.F01.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.VerifyPathTenant_acceptsMatch",   "fns": ["M00.F01.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantGuardTests.VerifyPathTenant_throwsWhenJwtMissing_evenInDev", "fns": ["M00.F01.I03"], "inert": False},

        # M04.F01 + M04.F02.I01 — admin OAuth client CRUD（tests/AdminClientsControllerTests.cs，2026-09-10 审计补齐）
        {"test": "Saas.Identity.AspNetCore.Tests.AdminClientsControllerTests.ClientsPost_persistsAndReturnsDto", "fns": ["M04.F01.I02"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.AdminClientsControllerTests.ClientsGet_listsPaged", "fns": ["M04.F01.I01"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.AdminClientsControllerTests.ClientsGet_byClientId_returnsDetail", "fns": ["M04.F01.I04"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.AdminClientsControllerTests.ClientsPatch_updatesName", "fns": ["M04.F01.I04"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.AdminClientsControllerTests.ClientsDelete_removesAndSets204", "fns": ["M04.F01.I05"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.AdminClientsControllerTests.Status_patchUpdatesStatus", "fns": ["M04.F02.I01"], "inert": False},

        # M00.F05 — 租户应用订阅 CRUD（tests/TenantApplicationsControllerTests.cs，2026-09-10 审计补齐）
        {"test": "Saas.Identity.AspNetCore.Tests.TenantApplicationsControllerTests.ApplicationsPost_subscribesClient", "fns": ["M00.F05.I02"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantApplicationsControllerTests.ApplicationsPost_unknownClient_throws404", "fns": ["M00.F05.I02"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantApplicationsControllerTests.ApplicationsPost_duplicateSubscription_throws", "fns": ["M00.F05.I02"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantApplicationsControllerTests.ApplicationsGet_listsTenantScoped", "fns": ["M00.F05.I01"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantApplicationsControllerTests.ApplicationsPatch_updatesStatusAndExpiry", "fns": ["M00.F05.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantApplicationsControllerTests.ApplicationsDelete_removesSubscription", "fns": ["M00.F05.I04"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.TenantApplicationsControllerTests.Applications_ops_guardRejectsForeignTenant", "fns": ["M00.F01.I03"], "inert": False},

        # M01.F04.I02 — FailedLoginStore (tests/Auth/Session/FailedLoginStoreTest.cs)
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.FailedLoginStoreTest.Record_thenGet_attemptsIncrements", "fns": ["M01.F04.I02"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.FailedLoginStoreTest.LockedUser_throwsLockedException", "fns": ["M01.F04.I02"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.FailedLoginStoreTest.LockoutExpires_afterDuration", "fns": ["M01.F04.I02"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.FailedLoginStoreTest.ResetSuccess_clearsCounter", "fns": ["M01.F04.I02"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.FailedLoginStoreTest.BelowThreshold_notLocked", "fns": ["M01.F04.I02"], "inert": False},

        # M01.F04.I03 — SaasSessionStore (tests/Auth/Session/SaasSessionStoreTest.cs) + Middleware (SaasSessionMiddlewareTest.cs)
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.SaasSessionStoreTest.Put_thenGet_returnsSameSession", "fns": ["M01.F04.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.SaasSessionStoreTest.Get_unknownId_returnsNull", "fns": ["M01.F04.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.SaasSessionStoreTest.Get_expiredSession_returnsNullAndRemoves", "fns": ["M01.F04.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.SaasSessionStoreTest.Delete_removesSession", "fns": ["M01.F04.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.SaasSessionStoreTest.GenerateId_returnsUniqueIds", "fns": ["M01.F04.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.SaasSessionMiddlewareTest.ValidCookie_injectsSessionIntoItems", "fns": ["M01.F04.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.SaasSessionMiddlewareTest.NoCookie_doesNotInject", "fns": ["M01.F04.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.SaasSessionMiddlewareTest.UnknownCookieValue_doesNotInject", "fns": ["M01.F04.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.SaasSessionMiddlewareTest.ExpiredCookie_doesNotInject", "fns": ["M01.F04.I03"], "inert": False},
        {"test": "Saas.Identity.AspNetCore.Tests.Auth.Session.SaasSessionMiddlewareTest.Next_invoked_always", "fns": ["M01.F04.I03"], "inert": False},

        # M04.F03 OAuth (9/7 重命名后: I01=授权码签发 / I02=令牌交换 / I03=令牌刷新) —
        # 当前 live 测试覆盖全在 tests/Auth/Session + tests/TenantGuard；
        # OauthControllerTests 整文件 #if false（pre-existing 烂测试）。真覆盖走 contract-test 仓
        # 跨端 live（start-family.sh + 4 后端 vitest）承担。M04.F03.I01/I02/I03 的 trace 锚点
        # 由 contract-test 仓 M96.F02.I21 + aspnetcore flow-function-map.md 登记承担。

        # M01.F03.I02 — 切换当前租户（tests/MeControllerSwitchTests.cs，2026-09-10 审计红线 #4 修复随附）
        {"test": "Saas.Identity.AspNetCore.Tests.MeControllerSwitchTests.Switch_returnsHs256Token_notAlgNone", "fns": ["M01.F03.I02"], "inert": False},
    ],
}

state_dir = ROOT / ".state"
state_dir.mkdir(exist_ok=True)
trace_path = state_dir / "trace.json"
trace_path.write_text(json.dumps(trace, indent=2) + "\n", encoding="utf-8")
print(f"[gen-trace] wrote {trace_path}", flush=True)
sys.exit(0)