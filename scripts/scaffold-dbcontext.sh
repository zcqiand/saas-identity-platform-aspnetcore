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

# dotnet ef 8.0.0 启动 design-time 子进程需要 multi-level SDK 解析；
# 用户 shell 若 export 了 DOTNET_MULTILEVEL_LOOKUP=0（VS Code / GitHub Actions 等常见），
# 会让 ef 找不到 design tools 路径而抛「Missing required option '--assembly'」。
# 脚本里强制 unset，让 dotnet ef 走默认 multi-level lookup。
unset DOTNET_MULTILEVEL_LOOKUP

# 不要用 `git rev-parse --show-toplevel` —— 本仓是 submodule，
# 该命令返回外层 xr-code-suite 根而不是本仓根，让 csproj 路径算错。
# 用脚本自身所在目录的父目录锚定到仓根。
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${SCRIPT_DIR}/.."

DB_PROJECT="src/Saas.Identity.AspNetCore.csproj"
# dotnet ef 的 --output-dir / --context-dir 是相对于 csproj 的；
# csproj 在 src/，所以目标相对路径是 Infrastructure/Persistence/Generated
OUTPUT_DIR="Infrastructure/Persistence/Generated"
GENERATED_DIR="src/${OUTPUT_DIR}"

# 默认连接（与 scripts/lib/db-env.sh 同套）
# env 优先；env 未设时，从仓根 .env.test / .env 读 DATABASE_URL 解析出 5 件套。
# 不写字面量默认密码（CLAUDE.md §2「禁止 env 默认值兜底」）；缺 DATABASE_URL 即报错退出，
# 让调用者补 DATABASE_URL（dev 调试）/ 检查 .env.test（CI）—— 不靠兜底让脚本「看起来过了」。
PG_HOST="${PG_HOST:-}"
PG_PORT="${PG_PORT:-}"
PG_DATABASE="${PG_DATABASE:-}"
PG_USER="${PG_USER:-}"
PG_PASSWORD="${PG_PASSWORD:-}"

if [ -z "${PG_PASSWORD}" ] || [ -z "${PG_USER}" ] || [ -z "${PG_DATABASE}" ] || [ -z "${PG_HOST}" ]; then
  ENV_FILE=""
  # .env.local 优先（dev 本地真 password，gitignored）；回退 .env.test（CI 占位）/.env（生产占位）
  for candidate in ".env.local" ".env.test" ".env"; do
    if [ -f "$candidate" ] && grep -q '^DATABASE_URL=' "$candidate"; then
      ENV_FILE="$candidate"
      break
    fi
  done
  if [ -z "$ENV_FILE" ]; then
    echo "[scaffold-dbcontext] ERROR: PG_PASSWORD/PG_USER/PG_DATABASE/PG_HOST 未在 env 设，" >&2
    echo "[scaffold-dbcontext]        仓根也找不到含 DATABASE_URL= 的 .env.test/.env/.env.local。" >&2
    echo "[scaffold-dbcontext]        fix: export DATABASE_URL='Host=...;...;Password=...' 或补 .env.test。" >&2
    exit 2
  fi
  echo "[scaffold-dbcontext] 从 ${ENV_FILE} 读 DATABASE_URL 解析连接 5 件套（env 优先于文件）"
  # DATABASE_URL 是 Key=Value; 形式，逐 key 提取（不引入 grep -P / python 依赖）。
  DATABASE_URL_VAL=$(grep -E '^DATABASE_URL=' "$ENV_FILE" | head -1 | cut -d= -f2-)
  for kv in $(echo "$DATABASE_URL_VAL" | tr ';' ' '); do
    k="${kv%%=*}"
    v="${kv#*=}"
    case "$k" in
      Host)     [ -z "$PG_HOST" ] && PG_HOST="$v" ;;
      Port)     [ -z "$PG_PORT" ] && PG_PORT="$v" ;;
      Database) [ -z "$PG_DATABASE" ] && PG_DATABASE="$v" ;;
      Username) [ -z "$PG_USER" ] && PG_USER="$v" ;;
      Password) [ -z "$PG_PASSWORD" ] && PG_PASSWORD="$v" ;;
    esac
  done
fi

# 兜底 host/port（其它 key 缺失时报错退出；host 是公开 IP 可硬编码）
PG_HOST="${PG_HOST:-100.79.128.25}"
PG_PORT="${PG_PORT:-5432}"
[ -z "$PG_DATABASE" ] && { echo "[scaffold-dbcontext] ERROR: Database= 缺失" >&2; exit 2; }
[ -z "$PG_USER" ] && { echo "[scaffold-dbcontext] ERROR: Username= 缺失" >&2; exit 2; }
[ -z "$PG_PASSWORD" ] && { echo "[scaffold-dbcontext] ERROR: Password= 缺失（CLAUDE.md 禁字面量兜底）" >&2; exit 2; }

CONNECTION="Host=${PG_HOST};Port=${PG_PORT};Database=${PG_DATABASE};Username=${PG_USER};Password=${PG_PASSWORD}"

# step 1/3 — 确保 Generated/ 存在（不 wipe）。
#
# 历史背景：本脚本早期会 `find ... -delete` 清空 Generated/ 含 AppDbContext.cs，
# 然后跑 `dotnet ef dbcontext scaffold`。问题：scaffold 跑前 build 必须先通，
# 而 Controllers/*.cs 已经在 using Generated/ 的 entity（Tenant/SysUser/SysMenu/...），
# wipe → build 报 CS0246 → scaffold 起不来 → 永远空仓。鸡生蛋。
#
# 现在做法：保留仓内 entity，靠 `dotnet ef dbcontext scaffold --force` 覆盖同名文件。
# 后果：DB 已 DROP 但仓残留的 orphan .cs 不会被 scaffold 自动删（脚手架只覆盖不删孤儿）。
# 这条不变量由 step 3 的 git diff + 仓维护者负责：
#   - 真要清孤儿：手动 `rm src/Infrastructure/Persistence/Generated/<OrphanEntity>.cs`
#   - 或者先跑一次 migrate 把孤儿表加回 DB 让 scaffold 一致（不推荐，违反 DB-First）
# orphan 不构成 DB drift；L4.db.scaffold 看的是「scaffold 成功 + git diff 可控」，
# 不是 orphan 数量。
#
# 手写 AppDbContext partial 在 sibling src/Infrastructure/Persistence/AppDbContext.cs
#（不在 Generated/），scaffold --force 不会动它。
if [ ! -d "$GENERATED_DIR" ]; then
  mkdir -p "$GENERATED_DIR"
  echo "[scaffold-dbcontext] step 1/3 — 创建 ${GENERATED_DIR}/（首次运行）"
else
  echo "[scaffold-dbcontext] step 1/3 — 保留 ${GENERATED_DIR}/（scaffold --force 覆盖同名；orphan 手动处理）"
fi

echo "[scaffold-dbcontext] step 2/3 — dotnet ef dbcontext scaffold"
# --verbose 让 dotnet ef 8.0.0 走不同的 design-time host 路径，绕过「Missing required option '--assembly'」bug。
# 该 bug 在 8.0.10+ 修复，但 csproj 锁 EF Core 8.0.0 时只能靠 --verbose 兜底。
# bug 表现：child process 写完所有 entity 文件到 Generated/ 后才 stderr 报 "Missing --assembly"，
# 然后 exit 1。脚本侧靠 `|| EF_EXIT_CODE=$?` 捕获真实退出码；若产物生成成功（ExpectedFiles
# 都在），放行 step 3，否则报错退出。
set +e
dotnet ef dbcontext scaffold \
    "$CONNECTION" \
    Npgsql.EntityFrameworkCore.PostgreSQL \
    --project "$DB_PROJECT" \
    --output-dir "$OUTPUT_DIR" \
    --context AppDbContext \
    --context-dir "$OUTPUT_DIR" \
    --force \
    --verbose
EF_EXIT_CODE=$?
set -e

# 验证产物是否真生成（不靠 exit code，靠 file presence）
EXPECTED=("AppDbContext.cs")
# 至少要有 1 个 entity .cs + AppDbContext.cs 才算 scaffold 成功
ENTITY_COUNT=$(find "$GENERATED_DIR" -maxdepth 1 -name "*.cs" -type f 2>/dev/null | wc -l)
if [ "$ENTITY_COUNT" -lt 2 ]; then
  echo "[scaffold-dbcontext] ERROR: dotnet ef exit=$EF_EXIT_CODE 且 Generated/ 产物 < 2（scaffold 未实际生成）" >&2
  exit "$EF_EXIT_CODE"
fi
echo "[scaffold-dbcontext]    dotnet ef exit=$EF_EXIT_CODE 但 Generated/ 有 $ENTITY_COUNT 个 .cs（--verbose bug 误报；产物 OK）"

# step 2.5/3: scaffold 模板把 Host/Port/Database/Username/Password 硬编码进 OnConfiguring
# expression body，违反 CLAUDE.md §2「禁止 env 默认值兜底」。scaffold 后立即 sed 把
# 整个表达式 body 替换成从 DATABASE_URL env 读取，#warning 包 #pragma disable。
# runtime 走 Program.cs DI（用 NpgsqlDataSourceBuilder 注入），本 OnConfiguring 仅 EF
# design-time 工具走 — 让 design-time 也走 env，与 runtime 路径对称。
APPDBCTX="${GENERATED_DIR}/AppDbContext.cs"
if [ -f "${APPDBCTX}" ]; then
  python3 - "${APPDBCTX}" <<'PYEOF'
import re, sys
p = sys.argv[1]
with open(p, encoding='utf-8') as f:
    src = f.read()

old_pattern = re.compile(
    r'(protected override void OnConfiguring\(DbContextOptionsBuilder optionsBuilder\)\n)'
    r'(#warning[^\n]*\n)'
    r'(\s*=> optionsBuilder\.UseNpgsql\("Host=[^"]*"\);)',
    re.MULTILINE,
)
new_text = (
    r'\1'
    '    {\n'
    '        // EF standard: explicit options (test InMemory / Program.cs DI NpgsqlDataSource)\n'
    '        // already configured — do not override. Only design-time tools (scaffold) with\n'
    '        // no options fall through to the env read + fail-fast below.\n'
    '        if (optionsBuilder.IsConfigured) return;\n'
    '#pragma warning disable CS1030 // scaffold 模板 #warning（连接串由 env 注入，CLAUDE.md §2 fail-fast）\n'
    r'\2'
    '#pragma warning restore CS1030\n'
    r'        optionsBuilder.UseNpgsql(\n'
    r'            System.Environment.GetEnvironmentVariable("DATABASE_URL")\n'
    r'                ?? throw new System.InvalidOperationException(\n'
    r'                    "DATABASE_URL 未设置。dev 加载 .env.test/.env.local；prod 由 deploy 脚本写入 VPS env-file。"\n'
    r'                    + "本规则遵循 CLAUDE.md §2 「禁止 env 默认值兜底」：secret 缺失必须 fail-fast。"\n'
    r'                    + "（runtime 走 Program.cs DI，不进 OnConfiguring；本路径仅 EF design-time 工具触达）"));\n'
    r'    }'
)
if old_pattern.search(src):
    src = old_pattern.sub(new_text, src, count=1)
    with open(p, 'w', encoding='utf-8') as f:
        f.write(src)
    print('[scaffold-dbcontext]    OnConfiguring 改 env 读取（清硬编码 password）+ IsConfigured guard（保 InMemory/DI 显式 options 不被覆盖）')
else:
    print('[scaffold-dbcontext]    WARN: OnConfiguring 模式未匹配，password 可能仍是硬编码（CLAUDE.md §2 风险）', file=sys.stderr)
PYEOF
fi

echo "[scaffold-dbcontext] step 3/3 — git diff src/${OUTPUT_DIR}/（DB-First 真源对照）"
if git diff --exit-code --quiet "src/${OUTPUT_DIR}/" 2>/dev/null; then
  echo "[scaffold-dbcontext] OK  无 diff（DB 与仓 entity 完全一致）"
else
  echo "[scaffold-dbcontext] OK  有 diff — git add src/${OUTPUT_DIR}/ && git commit 落仓"
  git diff --stat "src/${OUTPUT_DIR}/" || true
fi

# ADR-0026 §2: 写 last-gen-shared.json marker（DB 类别），失败不阻塞 scaffold。
ROOT="$(pwd)"
SHARED_DIR="$(cd "${ROOT}/../saas-identity-platform-shared" && pwd)"
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

# shared_sha 取「最近一次同步」对应的 sha：ISO-8601 UTC 时间戳字典序==时间序。
# 勿用 max(sha)——SHA 字典序不是 git 时间序（5.21 事故）。
entries = [
    (marker.get(k + "_at", ""), marker[k + "_sha"])
    for k in ("api_synced", "db_synced")
    if marker.get(k + "_sha")
]
marker["shared_sha"] = max(entries)[1] if entries else shared_sha
marker["consumer_repo"] = repo

with open(marker_path, "w", encoding="utf-8") as f:
    json.dump(marker, f, ensure_ascii=False, indent=2)
    f.write("\n")
PYEOF
then
  echo "[scaffold-dbcontext]    ADR-0026 marker 已落盘: $MARKER (shared HEAD ${SHARED_SHA:0:7})"
else
  echo "[scaffold-dbcontext]    WARN: marker 写失败（python3 缺失？）—— staleness 将报 UNKNOWN" >&2
fi
