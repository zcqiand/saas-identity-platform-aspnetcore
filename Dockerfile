# ===== saas-identity-platform-aspnetcore — ASP.NET Core 8 production image =====
# Multi-stage:
#   build    : dotnet:8.0-sdk → restore + publish self-contained=false
#   runtime  : aspnet:8.0 (debian-slim) → copy published app + ContentRoot
# 容器内监听 :8080 (ASPNETCORE_URLS=http://+:8080);VPS nginx 反代到 host 8024
# (saas-aspnetcore.xiangru.uk — 注意域名与 saas-springboot.xiangru.uk 区分,
#  别名都同 saas. 家族, 后端实现 swap 链)。

# ---------- Stage 1: builder ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder
WORKDIR /src

# 装 ca-certificates + git — slim 默认缺, git 让 NSwag 重 gen 时用 (本镜像已 gen 过,
# 但未来若 shared/openapi.yaml 变了可以 git clone 后 regen, 留个底)
RUN apt-get update -qq && apt-get install -y --no-install-recommends \
    git ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# 拷 csproj 先, restore 走 layer cache (csproj 不变就不重跑 restore)
COPY src/Saas.Identity.AspNetCore.csproj src/
RUN dotnet restore src/Saas.Identity.AspNetCore.csproj

# 拷全 + publish。Release + linux-x64 (容器是 debian-slim 但微软 8.0 镜像也是 debian,
# 所以 RID 默认即可)。Output 写到 /app/publish, ContentRootPath 用 AppContext.BaseDirectory
# 指 /app 让 appsettings*.json 也自动落到 /app。
COPY . .
RUN dotnet publish src/Saas.Identity.AspNetCore.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# appsettings*.json 在仓根, csproj 显式 Include ..\appsettings.json (Link=appsettings.json)
# 拷到 bin/, 已含在 publish 输出里。

# ---------- Stage 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# 非 root 跑（dotnet 镜像默认内置，非 0 即可；显式声明便于 security scan）
RUN groupadd --system --gid 1001 saasasp \
 && useradd  --system --uid 1001 --gid saasasp saasasp

# 从 builder 拷 publish 产物
COPY --from=builder --chown=saasasp:saasasp /app/publish /app

# 容器内监听 :8080（vite SPA 仓走 :80, 后端仓统一 :8080）。
# ContentRootPath=AppContext.BaseDirectory → /app, 自动找到 appsettings*.json。
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true

EXPOSE 8080

USER saasasp

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget --tries=1 --timeout=3 -qO- http://127.0.0.1:8080/health >/dev/null || exit 1

ENTRYPOINT ["dotnet", "Saas.Identity.AspNetCore.dll"]