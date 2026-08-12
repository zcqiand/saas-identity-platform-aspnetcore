# saas-identity-platform-aspnetcore

> ASP.NET Core 8 + xUnit + JwtBearer。消费 shared 仓 TypeSpec codegen 产物（C# record + Controller）。

## 1. 这是什么

saas-identity-platform 的 C# 后端。Controller 与 DTO 由 codegen 全覆盖；手写 Service。

## 2. 禁止事项

- 禁止手写 Controller
- 禁止在 Controller 写业务逻辑
- 禁止跳过 TenantGuard 校验

## 3. 指向别处

- shared 仓：`../saas-identity-platform-shared`
- function-tree：`docs/functions/function-tree.md`

## 4. 工作循环

1. 改 Service（`src/Service/*.cs`）
2. `bash scripts/gen-shared.sh`
3. `python scripts/gate.py -p saas-identity-platform-aspnetcore`
