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
#   - 数据库：PostgreSQL 远程, ConnectionStrings__Postgres 从 aspnetcore.env 注入
#     （EF Core / Npgsql 无 ./data 卷, 程序重启数据不丢）
#   - 容器内是 ASP.NET Core 8 监听 :8080 → -p 127.0.0.1:8024:8080
#   - 密钥走 ./aspnetcore.env (ConnectionStrings__Postgres + Jwt__* + Saas__Cors__AllowedOrigins),
#     setup-vps.sh 不预生成（fail-fast 不便）,本脚本首启自举。
#   - JWT_SIGNING_KEY **不**写入: Program.cs:32 dev 路径 = Program.cs:41 if (IsDevelopment)
#     显式覆盖 TokenValidationParameters 放 alg=none；prod 路径 = 删 dev 段 + 设
#     Jwt__Authority env 走真实对称 key / OIDC issuer-uri。本脚本只覆盖环境变量,
#     不动 C# 代码 —— prod 路径由独立 PR 处理。
#
# 前置: deploy 用户需在 docker 组中(sudo usermod -aG docker deploy)。
#        aspnetcore.env 必须由 setup-vps.sh 或本脚本首启生成(ConnectionStrings__Postgres 必填)。

set -eu

USERNAME="${1:-}"
PASSWORD="${2:-}"
VERSION="${3:-latest}"
IMAGE="${USERNAME}/saas-identity-platform-aspnetcore:${VERSION}"
BASE="/home/deploy/saas-identity-platform-aspnetcore"
CONTAINER_NAME="saas-identity-platform-aspnetcore"
HOST_PORT=8024

# nginx domain（deploy 脚本渲染 nginx vhost 时用）
NGINX_DOMAIN="${NGINX_DOMAIN:-saas-aspnetcore.xiangru.uk}"
NGINX_CERT_BASENAME="${NGINX_CERT_BASENAME:-xiangru-uk}"

if [ -z "$USERNAME" ] || [ -z "$PASSWORD" ]; then
  echo "Usage: $0 <DOCKER_USERNAME> <DOCKER_PASSWORD> [VERSION]" >&2
  exit 2
fi

# aspnetcore.env 自举保护: 缺失时, 如 $DATABASE_URL + $DATABASE_USER + $DATABASE_PASSWORD 在环境里,
# 自动生成（含 CORS 默认白名单）; 否则 fail fast。
# setup-vps.sh 仍是首推（VPS 一次性, 生成 nginx vhost + 目录 + sudoers）, 本分支仅给
# "先有 DATABASE_URL 临时上线"的场景。
if [ ! -f "$BASE/aspnetcore.env" ]; then
  if [ -n "${DATABASE_URL:-}" ] && [ -n "${DATABASE_USER:-}" ] && [ -n "${DATABASE_PASSWORD:-}" ]; then
    echo "→ bootstrapping $BASE/aspnetcore.env from env DATABASE_URL/USER/PASSWORD"
    umask 077
    {
      printf 'ConnectionStrings__Postgres=%s\n' "$DATABASE_URL"
      printf 'Jwt__Authority=https://auth.example.com\n'
      printf 'Jwt__Issuer=saas-identity-platform\n'
      printf 'Jwt__Audience=saas-identity-platform-clients\n'
      printf 'Saas__Cors__AllowedOrigins=https://%s,https://saas-vue.xiangru.uk,https://saas-react.xiangru.uk,https://saas-nextjs.xiangru.uk\n' "$NGINX_DOMAIN"
    } > "$BASE/aspnetcore.env"
    chown deploy:deploy "$BASE/aspnetcore.env" 2>/dev/null || true
    chmod 600 "$BASE/aspnetcore.env"
  else
    echo "ERROR: $BASE/aspnetcore.env missing. Set DATABASE_URL/USER/PASSWORD env (e.g. DATABASE_URL='Host=100.79.128.25;Port=5432;Database=saas_prod;Username=postgres;Password=...' DATABASE_USER=postgres DATABASE_PASSWORD=... sudo -E sh deploy/setup-vps.sh saas-aspnetcore.example.com) or run setup-vps.sh first." >&2
    exit 1
  fi
fi
# 校验 aspnetcore.env 里有 ConnectionStrings__Postgres（即使 env-file 已存在, 内容可能是上一次失败留下的）
if ! grep -q '^ConnectionStrings__Postgres=' "$BASE/aspnetcore.env"; then
  echo "ERROR: $BASE/aspnetcore.env has no ConnectionStrings__Postgres line" >&2
  exit 1
fi

# nginx vhost 自举（缺时创建, 不 reload —— reload 要 root）:
# 检测 /etc/nginx/sites-enabled/<NGINX_DOMAIN> 是否存在; 缺时从 nginx-vps.conf.example
# 模板渲染, 做 symlink。reload 需 sudo, 留给手工:
#   sudo nginx -t && sudo systemctl reload nginx
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

if [ -e "${NGINX_VHOST_LINK}" ] || [ -e "${NGINX_VHOST_FILE}" ]; then
  echo "→ nginx vhost ${NGINX_VHOST_FILE} already exists, skip bootstrap"
else
  echo "→ nginx vhost missing, bootstrapping ${NGINX_VHOST_FILE} (domain=${NGINX_DOMAIN} cert=${NGINX_CERT_BASENAME})"
  # deploy 用户默认没有写 /etc/nginx/sites-available/ 的权限。`>` 重定向在 dash 下
  # 失败时 -e 不传播 → 文件静默没生成 → CI 显示 success 但站点 404。修法：先
  # 检测目录可写，否则用 sudo cp + sudo ln -sf 提权（admin 操作走 sudoers 白名单）
  if [ -w "${NGINX_SITES_AVAILABLE}" ]; then
    umask 022
    sed \
      -e "s/saas.YOUR_DOMAIN/${NGINX_DOMAIN}/g" \
      -e "s|/etc/nginx/ssl/your-cert.cert|/etc/nginx/ssl/${NGINX_CERT_BASENAME}.cert|g" \
      -e "s|/etc/nginx/ssl/your-cert.key|/etc/nginx/ssl/${NGINX_CERT_BASENAME}.key|g" \
      "${NGINX_TEMPLATE}" > "${NGINX_VHOST_FILE}"
    echo "→ wrote ${NGINX_VHOST_FILE} (direct, deploy user has write perms)"
  else
    echo "→ ${NGINX_SITES_AVAILABLE} not writable by $(id -un); need sudo (ensure /etc/sudoers.d/deploy-nginx allows: deploy ALL=(ALL) NOPASSWD: /bin/cp /bin/ln)"
    TMP_VHOST="$(mktemp)"
    sed \
      -e "s/saas.YOUR_DOMAIN/${NGINX_DOMAIN}/g" \
      -e "s|/etc/nginx/ssl/your-cert.cert|/etc/nginx/ssl/${NGINX_CERT_BASENAME}.cert|g" \
      -e "s|/etc/nginx/ssl/your-cert.key|/etc/nginx/ssl/${NGINX_CERT_BASENAME}.key|g" \
      "${NGINX_TEMPLATE}" > "${TMP_VHOST}"
    sudo cp "${TMP_VHOST}" "${NGINX_VHOST_FILE}" \
      && echo "→ wrote ${NGINX_VHOST_FILE} (via sudo cp)" \
      || { echo "→ ERROR: failed to write ${NGINX_VHOST_FILE}"; exit 1; }
    rm -f "${TMP_VHOST}"
  fi
  if [ -w "${NGINX_SITES_ENABLED}" ]; then
    ln -sf "${NGINX_VHOST_FILE}" "${NGINX_VHOST_LINK}"
    echo "→ linked ${NGINX_VHOST_LINK} (direct)"
  else
    sudo ln -sf "${NGINX_VHOST_FILE}" "${NGINX_VHOST_LINK}" \
      && echo "→ linked ${NGINX_VHOST_LINK} (via sudo ln)" \
      || { echo "→ ERROR: failed to link ${NGINX_VHOST_LINK}"; exit 1; }
  fi
  echo "→ nginx vhost created. To enable: sudo nginx -t && sudo systemctl reload nginx"
fi

# 必要时补 Saas__Cors__AllowedOrigins（Program.cs 走 Saas:Cors:AllowedOrigins 通过 configuration binding）。
# 已有则不覆盖（bootstrap 那段 line 67-85 首启会写; 后续 deploy 重跑只会缺失时 append,
# 运维手工补的 prod origin 不会丢）。
if ! grep -q '^Saas__Cors__AllowedOrigins=' "$BASE/aspnetcore.env"; then
  echo "→ append Saas__Cors__AllowedOrigins to existing $BASE/aspnetcore.env"
  umask 077
  printf 'Saas__Cors__AllowedOrigins=https://%s,https://saas-vue.xiangru.uk,https://saas-react.xiangru.uk,https://saas-nextjs.xiangru.uk\n' "$NGINX_DOMAIN" >> "$BASE/aspnetcore.env"
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
  -p "127.0.0.1:${HOST_PORT}:8080" \
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
  if wget --tries=1 --timeout=3 -q "http://127.0.0.1:${HOST_PORT}/health" -O /dev/null 2>/dev/null; then
    echo "→ /health 200 (host 127.0.0.1:${HOST_PORT}) after ${i}s"
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
if wget --tries=1 --timeout=3 -q "http://127.0.0.1:${HOST_PORT}/swagger/v1/swagger.json" -O /dev/null 2>/dev/null; then
  echo "→ swagger /swagger/v1/swagger.json 200"
else
  echo "→ WARNING: swagger 探针失败（v0.1.5 后应可用, 可能是 dev JwtBearer 配置阻挡）"
fi

echo "→ deploy done at $(date -u)"