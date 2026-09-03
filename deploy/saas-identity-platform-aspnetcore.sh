#!/bin/sh
# Usage: saas-identity-platform-aspnetcore.sh <DOCKER_USERNAME> <DOCKER_PASSWORD> [VERSION]
#
# 由 .github/workflows/ci.yml 的 deploy job 远程调用:
#   ssh deploy@vps -- cd /home/deploy/saas-identity-platform-aspnetcore
#                    && sh saas-identity-platform-aspnetcore.sh $DOCKER_USERNAME $DOCKER_PASSWORD $VERSION
#
# VERSION 默认是 latest。tag-based deploy 时显式传 tag 名（v0.1.x-YYYYMMDD）。
# CI 同时 push :latest + :<tag> 两份镜像,回滚只要手动指定旧 tag 再跑一次本脚本。
#
# 与姊妹仓 saas-identity-platform-springboot.sh 的差异:
#   - 数据库：PostgreSQL 远程, DATABASE_URL 从 aspnetcore.env 注入
#     （EF Core / Npgsql 无 ./data 卷, 程序重启数据不丢）
#   - 容器内 ASP.NET Core 8 监听 :5104（conventions §6）；host=container=5104
#     （ADR-0018 单层 port 方案，docker run -p 127.0.0.1:5104:5104；saas 家族 X04 段）
#   - 密钥走 ./aspnetcore.env (DATABASE_URL + JWT_SIGNING_KEY + flat JWT_* +
#     SAAS_CORS_ALLOWED_ORIGINS, key 集合与仓内 .env.production 对齐),
#     setup-vps.sh 不预生成（fail-fast 不便）,本脚本首启自举。
#   - JWT_SIGNING_KEY 必填(GitHub Secret → ssh-action envs): v0.2.1 已删 dev
#     alg=none 分支,现统一 HS256 真验签,JwtIssuer.cs:39 fail-fast。
#
# 前置: deploy 用户需在 docker 组中(sudo usermod -aG docker deploy)。
#        aspnetcore.env 必须由 setup-vps.sh 或本脚本首启生成(DATABASE_URL 必填)。

set -eu

USERNAME="${1:-}"
PASSWORD="${2:-}"
VERSION="${3:-latest}"
IMAGE="${USERNAME}/saas-identity-platform-aspnetcore:${VERSION}"
BASE="/home/deploy/saas-identity-platform-aspnetcore"
CONTAINER_NAME="saas-identity-platform-aspnetcore"

# nginx domain（deploy 脚本渲染 nginx vhost 时用）
NGINX_DOMAIN="${NGINX_DOMAIN:-saas-aspnetcore.xiangru.uk}"
NGINX_CERT_BASENAME="${NGINX_CERT_BASENAME:-xiangru-uk}"

if [ -z "$USERNAME" ] || [ -z "$PASSWORD" ]; then
  echo "Usage: $0 <DOCKER_USERNAME> <DOCKER_PASSWORD> [VERSION]" >&2
  exit 2
fi

# aspnetcore.env 自举保护: 缺失时, 如 $DATABASE_URL + $JWT_SIGNING_KEY 在环境里,
# 自动生成（key 集合与仓内 .env.production 严格对齐,suite L0.5 check_deploy_parity
# 锁死,本地加 key 必须同步本脚本）; 否则 fail fast 指明缺哪个。
# （DATABASE_USER/PASSWORD 不需要: 连接串内嵌凭据, 2026-08-28 删冗余 secrets。）
# 禁默认值兜底: 非 secret 走已知 prod 字面量,secret（JWT_SIGNING_KEY）必须显式传入
# —— JwtIssuer.cs:39 fail-fast,缺了容器起不来,绝不静默吃代码 fallback。
# 老契约迁移提示: 旧 env-file 里是 Jwt__Authority/Issuer/Audience + Saas__Cors__*
# 分段 key（Program.cs 读 flat JWT_*/SAAS_CORS_ALLOWED_ORIGINS, 分段无读者）——
# 二选一: 手工改 key 名, 或备份后删掉 env-file 带 secrets 重跑本脚本重建。
# 改后必须走本脚本重建容器（--env-file 只在 create 时读, restart 不重读）。
if [ ! -f "$BASE/aspnetcore.env" ]; then
  # v0.2.1 已删 dev alg=none 分支（Program.cs Phase 2B 注释）,JWT_SIGNING_KEY 是
  # HS256 验签必需; ASPNETCORE_ENVIRONMENT=Development 不再写（dev 段已不存在）。
  if [ -z "${DATABASE_URL:-}" ]; then
    echo "ERROR: $BASE/aspnetcore.env missing and DATABASE_URL env not set (GitHub Secret DATABASE_URL → ssh-action envs)" >&2
    exit 1
  fi
  if [ -z "${JWT_SIGNING_KEY:-}" ]; then
    echo "ERROR: $BASE/aspnetcore.env missing and JWT_SIGNING_KEY env not set (add GitHub Secret JWT_SIGNING_KEY → ci.yml envs → ssh-action envs; JwtIssuer.cs fail-fast, 缺了容器起不来)" >&2
    exit 1
  fi
  echo "→ bootstrapping $BASE/aspnetcore.env (key 集合 = .env.production, 9 key)"
  umask 077
  {
    printf 'DATABASE_URL=%s\n' "$DATABASE_URL"
    printf 'JWT_SIGNING_KEY=%s\n' "$JWT_SIGNING_KEY"
    # flat key 与 Program.cs 读者一致（2026-08-28 断链修复: 曾写 Jwt__* 分段 key 无读者）
    printf 'JWT_AUTHORITY=https://auth.example.com\n'
    printf 'JWT_ISSUER=saas-identity-platform\n'
    printf 'JWT_AUDIENCE=saas-identity-platform-clients\n'
    printf 'JWT_TTL_SECONDS=3600\n'
    printf 'SERVER_PORT=5104\n'
    printf 'DATABASE_NAME=saas_prod\n'
    printf 'SAAS_CORS_ALLOWED_ORIGINS=https://%s,https://saas-vue.xiangru.uk,https://saas-react.xiangru.uk,https://saas-nextjs.xiangru.uk\n' "$NGINX_DOMAIN"
  } > "$BASE/aspnetcore.env"
  chown deploy:deploy "$BASE/aspnetcore.env" 2>/dev/null || true
  chmod 600 "$BASE/aspnetcore.env"
fi
# 校验 aspnetcore.env 里有 DATABASE_URL（即使 env-file 已存在, 内容可能是上一次失败留下的）
if ! grep -q '^DATABASE_URL=' "$BASE/aspnetcore.env"; then
  echo "ERROR: $BASE/aspnetcore.env has no DATABASE_URL line" >&2
  exit 1
fi

# nginx vhost 重渲染（每次 deploy 都跑,ADR-0018:容器端口变了 vhost 必须跟）:
# 模板从 master 拉,渲染后写入 sites-available,symlink sites-enabled,再 sudo nginx -t + reload。
# diff 检测:内容未变跳过 reload (nginx -t 也省)。
NGINX_SITES_AVAILABLE="/etc/nginx/sites-available"
NGINX_SITES_ENABLED="/etc/nginx/sites-enabled"
NGINX_VHOST_FILE="${NGINX_SITES_AVAILABLE}/${NGINX_DOMAIN}"
NGINX_VHOST_LINK="${NGINX_SITES_ENABLED}/${NGINX_DOMAIN}"
NGINX_TEMPLATE="${BASE}/nginx-vps.conf.example"

# 拉模板（deploy/ 目录随仓库 deploy 脚本一起, 但首次拉时可能不存在, 补一下）
if [ ! -f "${NGINX_TEMPLATE}" ]; then
  echo "→ fetching nginx-vps.conf.example template"
  curl -fsSL "https://raw.githubusercontent.com/zcqiand/saas-identity-platform-aspnetcore/refs/heads/master/deploy/nginx-vps.conf.example" -o "${NGINX_TEMPLATE}"
fi

# 渲染到临时文件 —— sed 同时覆盖 3 种 placeholder:
#   Style A (lab-vue/react):      <domain>
#   Style B/C (nextjs/sp/aspc):   lab.YOUR_DOMAIN / saas.YOUR_DOMAIN
#   cert 路径: your-cert.{crt,cert} / <domain>.crt → 统一到 ${NGINX_CERT_BASENAME}.cert
TMP_VHOST="$(mktemp -t vpstpl.XXXXXX)"
# cert 归一化规则必须排在 <domain>/YOUR_DOMAIN 通配之前:sed -e 按顺序执行,
# 先替换 <domain> 会把 cert 路径里的占位符一并吃掉,后面的 cert 规则全部失配
# (2026-09-03 VPS nginx -t "cannot load certificate <域名>.crt" 事故根因)
sed \
  -e "s|/etc/nginx/ssl/<domain>\.crt|/etc/nginx/ssl/${NGINX_CERT_BASENAME}.cert|g" \
  -e "s|/etc/nginx/ssl/<domain>\.cert|/etc/nginx/ssl/${NGINX_CERT_BASENAME}.cert|g" \
  -e "s|/etc/nginx/ssl/<domain>\.key|/etc/nginx/ssl/${NGINX_CERT_BASENAME}.key|g" \
  -e "s|/etc/nginx/ssl/your-cert\.crt|/etc/nginx/ssl/${NGINX_CERT_BASENAME}.cert|g" \
  -e "s|/etc/nginx/ssl/your-cert\.cert|/etc/nginx/ssl/${NGINX_CERT_BASENAME}.cert|g" \
  -e "s|/etc/nginx/ssl/your-cert\.key|/etc/nginx/ssl/${NGINX_CERT_BASENAME}.key|g" \
  -e "s|<domain>|${NGINX_DOMAIN}|g" \
  -e "s|lab\.YOUR_DOMAIN|${NGINX_DOMAIN}|g" \
  -e "s|saas\.YOUR_DOMAIN|${NGINX_DOMAIN}|g" \
  "${NGINX_TEMPLATE}" > "${TMP_VHOST}"

# diff 检测:已有 vhost 且内容相同就 skip,不同才重写 + reload
if [ -e "${NGINX_VHOST_FILE}" ] && diff -q "${TMP_VHOST}" "${NGINX_VHOST_FILE}" >/dev/null 2>&1; then
  echo "→ nginx vhost ${NGINX_VHOST_FILE} unchanged, skip"
  rm -f "${TMP_VHOST}"
else
  echo "→ rendering nginx vhost ${NGINX_VHOST_FILE} (domain=${NGINX_DOMAIN} cert=${NGINX_CERT_BASENAME})"
  # 写入 sites-available (deploy 用户可能没写权限,需要 sudoers 配 nginx 白名单)
  if [ -w "${NGINX_SITES_AVAILABLE}" ]; then
    cp "${TMP_VHOST}" "${NGINX_VHOST_FILE}"
  else
    sudo cp "${TMP_VHOST}" "${NGINX_VHOST_FILE}" \
      || { echo "ERROR: sudo cp ${NGINX_VHOST_FILE} failed"; rm -f "${TMP_VHOST}"; exit 1; }
  fi
  # symlink sites-enabled
  if [ -w "${NGINX_SITES_ENABLED}" ]; then
    ln -sf "${NGINX_VHOST_FILE}" "${NGINX_VHOST_LINK}"
  else
    sudo ln -sf "${NGINX_VHOST_FILE}" "${NGINX_VHOST_LINK}" \
      || { echo "ERROR: sudo ln ${NGINX_VHOST_LINK} failed"; rm -f "${TMP_VHOST}"; exit 1; }
  fi
  rm -f "${TMP_VHOST}"
  # nginx config test + reload (CI 自动完成,不再依赖手工)
  echo "→ nginx -t"
  sudo nginx -t
  echo "→ systemctl reload nginx"
  sudo systemctl reload nginx
  echo "✓ nginx reloaded"
fi

# v0.2.12 key 对齐迁移(2026-08-28): 老 env-file 逐 key append-if-missing 到
# .env.production 全集(key 集合契约由 suite L0.5 check_deploy_parity 锁死)。
# JWT_SIGNING_KEY 是 secret:老文件已有则保留(换 key 会让所有登录态失效);
# 没有则必须从 env 传入,fail-fast —— 不允许静默兜底。
if [ -f "$BASE/aspnetcore.env" ]; then
  append_if_missing() {
    key="$1"; val="$2"
    if ! grep -q "^${key}=" "$BASE/aspnetcore.env"; then
      echo "→ append ${key} to existing $BASE/aspnetcore.env"
      umask 077
      printf '%s=%s\n' "$key" "$val" >> "$BASE/aspnetcore.env"
    fi
  }
  if ! grep -q '^JWT_SIGNING_KEY=' "$BASE/aspnetcore.env"; then
    if [ -z "${JWT_SIGNING_KEY:-}" ]; then
      echo "ERROR: JWT_SIGNING_KEY missing in $BASE/aspnetcore.env and not set in env (JwtIssuer.cs fail-fast)" >&2
      exit 1
    fi
    append_if_missing JWT_SIGNING_KEY "$JWT_SIGNING_KEY"
  fi
  append_if_missing JWT_AUTHORITY 'https://auth.example.com'
  append_if_missing JWT_ISSUER 'saas-identity-platform'
  append_if_missing JWT_AUDIENCE 'saas-identity-platform-clients'
  append_if_missing JWT_TTL_SECONDS '3600'
  append_if_missing SERVER_PORT '5104'
  append_if_missing DATABASE_NAME 'saas_prod'
  # CORS:老值保留(运维可能手工补过 prod origin),只在缺失时写默认白名单
  if ! grep -q '^SAAS_CORS_ALLOWED_ORIGINS=' "$BASE/aspnetcore.env"; then
    append_if_missing SAAS_CORS_ALLOWED_ORIGINS "https://${NGINX_DOMAIN},https://saas-vue.xiangru.uk,https://saas-react.xiangru.uk,https://saas-nextjs.xiangru.uk"
  fi
  # 死键清理:分段 key 无读者(Program.cs 读 flat),Saas__Cors__* 与 ASPNETCORE_ENVIRONMENT
  # 一并删除,保 key 集合与 .env.production 对齐
  for dead in Saas__Cors__AllowedOrigins ASPNETCORE_ENVIRONMENT; do
    if grep -q "^${dead}=" "$BASE/aspnetcore.env"; then
      echo "→ drop dead key ${dead} from $BASE/aspnetcore.env"
      umask 077
      sed -i "/^${dead}=/d" "$BASE/aspnetcore.env"
    fi
  done
fi

echo "→ image: $IMAGE"
echo "→ docker login"
printf '%s' "$PASSWORD" | docker login -u "$USERNAME" --password-stdin

echo "→ docker pull"
docker pull "$IMAGE"

echo "→ docker stop & rm $CONTAINER_NAME"
docker stop "$CONTAINER_NAME" 2>/dev/null || true
docker rm "$CONTAINER_NAME" 2>/dev/null || true

echo "→ docker run"
docker run -d \
  --name "$CONTAINER_NAME" \
  --restart unless-stopped \
  -p "127.0.0.1:5104:5104" \
  --env-file "$BASE/aspnetcore.env" \
  "$IMAGE"

echo "→ docker image prune"
docker image prune -f

echo "→ docker ps"
docker ps --filter name="$CONTAINER_NAME"

# 健康检查: 直接 wget /health 探 200, 不依赖 Docker HEALTHCHECK 语义。
# Program.cs: MapGet("/health", ...) 匿名端点,无 auth 阻挡。
# 与 springboot 的 /actuator/health 同模式;但 aspnetcore 走最小 /health endpoint,
# 没有 Spring Boot 那么详细的 status JSON (UP/DOWN),单纯 200 即可。
i=0
while [ $i -lt 60 ]; do
  if wget --tries=1 --timeout=3 -q "http://127.0.0.1:5104/health" -O /dev/null 2>/dev/null; then
    echo "→ /health 200 (host 127.0.0.1:5104) after ${i}s"
    break
  fi
  # 容器实际死亡 (OOM / start-cmd failure / 立刻 crash) 提前终止循环, 立刻报失败。
  if ! docker inspect --format='{{.State.Running}}' "$CONTAINER_NAME" 2>/dev/null | grep -q true; then
    echo "→ container not running, logs:"
    docker logs --tail 30 "$CONTAINER_NAME"
    exit 1
  fi
  i=$((i+1))
  sleep 1
done

if [ $i -ge 60 ]; then
  echo "→ /health 仍未 200（60s 上限）, logs:"
  docker logs --tail 30 "$CONTAINER_NAME"
  exit 1
fi

# 额外探针: /swagger 看 OpenAPI UI 在线（v0.1.5+ 接了 Swashbuckle）
if wget --tries=1 --timeout=3 -q "http://127.0.0.1:5104/swagger/v1/swagger.json" -O /dev/null 2>/dev/null; then
  echo "→ swagger /swagger/v1/swagger.json 200"
else
  echo "→ WARNING: swagger 探针失败（v0.1.5 后应可用, 可能是 dev JwtBearer 配置阻挡）"
fi

echo "→ deploy done at $(date -u)"