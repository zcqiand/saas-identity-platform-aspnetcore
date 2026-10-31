#!/usr/bin/env python3
"""跑 dotnet test（TRX logger）并产出 .state/trace.json。

trace 契约（见 suite scripts/lib/harness.py）：
  - 每个测试一行：{"test": <完全限定名>, "fns": [功能 ID...], "inert": bool}
  - inert=true（skip/未执行）的测试 fns 必须为空数组 —— 假绿在源头抹掉

两段式设计（实测 xunit 2.9 + VSTest 17.10 的 [Trait] 不进 TRX）：
  1) outcome 从 TRX 拿 —— dotnet test --logger trx 的权威执行结果
  2) Fn ID 从源码拿 —— 正则扫 tests/**/*.cs 的 [Trait("Fn", "...")]，
     拼 namespace.class.method 完全限定名。功能 ID 的真相源本来就是源码，
     不依赖 runner 把 trait 写进哪个 XML 节点。

功能 ID 的挂法（可多个，方法级）：

    [Fact]
    [Trait("Fn", "M01.F01.I01")]
    [Trait("Fn", "M01.F01.I02")]
    public void CreateXxx_returnsOk() { ... }

Theory 展开的多行结果用 testName 前缀匹配挂到同一个方法上。
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TESTS_DIR = ROOT / "tests"
TRX_DIR = TESTS_DIR / ".trx"
STATE_DIR = ROOT / ".state"

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

# [Trait("Fn", "M01.F01.I01")] —— 允许单引号/双引号、大小写变体
TRAIT_RE = re.compile(r'\[\s*Trait\s*\(\s*"(?P<key>[^"]*)"\s*,\s*"(?P<val>[^"]*)"\s*\)\s*\]')
NAMESPACE_RE = re.compile(r"^\s*namespace\s+([\w.]+)", re.MULTILINE)
CLASS_RE = re.compile(r"^\s*(?:public|internal|partial|abstract|sealed|static|\s)*class\s+(\w+)", re.MULTILINE)
METHOD_RE = re.compile(r"^\s*(?:public|internal|private|protected|\s)*(?:async\s+)?(?:Task|ValueTask|void)\s+(\w+)\s*\(", re.MULTILINE)


def find_tests_csproj() -> Path:
    """动态发现测试工程（5.126 反灌批改：仓会重命名 csproj——Lab.AspNetCore.Tests/
    Saas.Identity.AspNetCore.Tests 各异，模板硬编码 App.Tests.csproj 已证陈旧）。
    约定 tests/ 下有且仅有一个 .csproj，多/零个 fail-fast。"""
    candidates = sorted(TESTS_DIR.glob("*.csproj"))
    if len(candidates) != 1:
        raise SystemExit(f"[gen-trace] tests/ 下期望恰 1 个 .csproj，实得 {len(candidates)}: "
                         f"{[c.name for c in candidates]}")
    return candidates[0]


def run_tests() -> int:
    """跑 dotnet test，TRX 落到 tests/.trx/。返回退出码（0 = 全绿）。"""
    cmd = [
        "dotnet",
        "test",
        str(find_tests_csproj()),
        "--nologo",
        "--logger",
        "trx",
        "--results-directory",
        str(TRX_DIR),
        "-v",
        "q",
    ]
    print(f"[gen-trace] {' '.join(cmd)}", flush=True)
    return subprocess.run(cmd, shell=sys.platform == "win32").returncode


def scan_fn_traits() -> dict[str, list[str]]:
    """扫 tests/**/*.cs：完全限定方法名 → [Fn 值]。

    文件粒度解析：一个文件假设一个 namespace（多个的取第一个），
    class 嵌套按「最近一个 class 声明在上文」近似 —— 测试代码习惯
    一文件一类一类一 namespace，够用且简单。
    """
    table: dict[str, list[str]] = {}
    for cs in sorted(TESTS_DIR.rglob("*.cs")):
        if "obj" in cs.parts or "bin" in cs.parts:
            continue
        src = cs.read_text(encoding="utf-8", errors="replace")
        ns_m = NAMESPACE_RE.search(src)
        ns = ns_m.group(1) if ns_m else ""
        # 逐字符位置找 class 声明与方法 + Trait 的配对：
        # 方法名匹配之前、该 class 作用域内的所有 Fn trait 都挂上去。
        classes = [(m.start(), m.group(1)) for m in CLASS_RE.finditer(src)]
        methods = list(METHOD_RE.finditer(src))
        for i, tm in enumerate(methods):
            method = tm.group(1)
            # 该方法声明点之前最近的 class
            cls = ""
            for pos, cname in classes:
                if pos < tm.start():
                    cls = cname
            if not cls:
                continue
            # 只取「上一个方法声明之后 → 本方法声明之前」的 Fn traits。
            # 取整个文件头会把同 class 前面方法的 trait 重复挂给本方法。
            seg_start = methods[i - 1].start() if i > 0 else 0
            seg = src[seg_start : tm.start()]
            fns = [m.group("val") for m in TRAIT_RE.finditer(seg) if m.group("key") == "Fn"]
            if not fns:
                continue
            full = f"{ns}.{cls}.{method}" if ns else f"{cls}.{method}"
            table[full] = sorted(set(fns))
    return table


def parse_trx(trx: Path, fn_traits: dict[str, list[str]]) -> list[dict]:
    """TRX outcome × 源码 trait 表 → trace 行。outcome ≠ Passed → inert。"""
    root = ET.parse(trx).getroot()
    rows: list[dict] = []
    for res in root.findall(".//t:UnitTestResult", NS):
        name = res.get("testName", "")
        outcome = res.get("outcome", "")
        inert = outcome != "Passed"
        fns: list[str] = []
        if not inert:
            # 精确匹配，或 Theory 前缀匹配（testName 带数据后缀）
            if name in fn_traits:
                fns = fn_traits[name]
            else:
                for full, ids in fn_traits.items():
                    if name.startswith(full):
                        fns = ids
                        break
        rows.append({"test": name, "fns": fns, "inert": inert})
    return rows


def main() -> int:
    # 陈旧 TRX 清场 —— 只认本次运行的产物
    if TRX_DIR.exists():
        for old in TRX_DIR.glob("*.trx"):
            old.unlink()

    fn_traits = scan_fn_traits()
    print(f"[gen-trace] 源码扫描：{len(fn_traits)} 个方法挂了 Fn trait", flush=True)

    rc = run_tests()
    if rc != 0:
        print("[gen-trace] dotnet test 失败（详见上方输出）。不产出 trace.json。", file=sys.stderr)
        return rc

    trxs = sorted(TRX_DIR.glob("*.trx"))
    if not trxs:
        print(
            "[gen-trace] dotnet test 成功但没找到 TRX 文件。检查 --results-directory 是否生效。",
            file=sys.stderr,
        )
        return 1

    all_rows: list[dict] = []
    for trx in trxs:
        all_rows.extend(parse_trx(trx, fn_traits))

    trace = {"schema": 1, "tests": all_rows}
    STATE_DIR.mkdir(exist_ok=True)
    out = STATE_DIR / "trace.json"
    out.write_text(json.dumps(trace, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"[gen-trace] wrote {out} ({len(all_rows)} tests)", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
