# PLAN — SaaS 多租户多应用身份平台 · ASP.NET Core 后端

> 待办与迭代方向。详细上下文见 `.state/session.json` 与 `docs/adr/`。

## 待办

### [BUG] User / Role POST 漏赋 `CreatedAt` / `UpdatedAt`,响应返回 `0001-01-01T00:00:00+00:00`

- **状态**: 待修复
- **首次发现**: 2026-09-01 live mode 全量 contract-test run
- **关联 ADR**: [docs/adr/0015-amend-timestamps.md](../../../docs/adr/0015-amend-timestamps.md) §「已知非契约问题」
- **关联合约测试**: `M96.F02.I15` `I16` `I19` `I32` `I34` `I57` `I71` 七处

### 症状(活证据)

live mode 全量跑:

```
[body] aspnetcore: createdAt: 时间戳 年份超出 [2000,2100] (0001-01-01T00:00:00+00:00)
[body] aspnetcore: updatedAt: 时间戳 年份超出 [2000,2100] (0001-01-01T00:00:00+00:00)
```

`+00:00` 后缀是 `DateTimeOffset` 的 STJ round-trip 默认格式(注意:`DateTime` 是 `Z` 后缀,这里 `DateTimeOffset` 是 `+00:00`,根因确认是值类型未赋值 = `MinValue`,不是类型选错)。

### 跨语言 MinValue 对照(2026-09-01 user 拍板,下个会话调试参考)

| Language / Framework | Minimum Date Value | Standard Code Representation | 本仓抓到的具体字符串 | 备注 |
|---|---|---|---|---|
| **C# (`DateTime`)** | `0001-01-01 00:00:00` | `DateTime.MinValue` | (本仓不用) | 无 T 分隔、无时区偏移。STJ `DateTime` round-trip 输出 `0001-01-01T00:00:00.0000000`(无偏移后缀)。 |
| **C# (`DateTimeOffset`)★本仓** | `0001-01-01 00:00:00 +00:00` | `DateTimeOffset.MinValue` | `0001-01-01T00:00:00+00:00` | STJ round-trip 加 `T` + `+00:00`。`+00:00` 是 `DateTimeOffset` 默认 UTC offset 输出格式(不是 `Z`)。 |
| Java (`java.time`) | `-999999999-01-01T00:00:00` | `LocalDateTime.MIN` | `-292275055-05-16T23:00:00Z` | springboot 仓抓到不是 `LocalDateTime.MIN` 标准 toString。真凶 Hibernate 6 + PG `-infinity` 经 `OffsetDateTime` 映射。详见 [springboot PLAN.md 跨语言表](../saas-identity-platform-springboot/PLAN.md) |
| Next.js (JS/TS) | `-271821-04-20T00:00:00Z` | `new Date(-8640000000000000)` | 未实测(本会话 nextjs 没起) | contract-test 抓到 nextjs `createdAt = undefined`(Drizzle `defaultNow()` 未填上推测),session.json 已记。 |

**调试提示**:contract-test `assertTimestampShape` 用 `年份 [2000,2100]` 断言抓所有 MinValue —— 上述 3 个语言 MinValue 都不在区间,所以都能抓到。本仓用 `DateTimeOffset`(值类型,非可空),OpenAPI `format: date-time` → NSwag 生成 `DateTimeOffset`(不是 nullable),赋值兜底成 `MinValue` 时 STJ round-trip 输出 `0001-01-01T00:00:00.0000000+00:00`。下次会话调试时不要被「年份 0001」与 Java `-292275055` 的差距迷惑——两者各属不同语言/序列化器组合。

### 已知事实

- 仓内 12 个 entity 的时间列全部用 `DateTimeOffset`(非 `DateTime`),PG 列是 `timestamptz`(见 `src/Migrations/20260830070144_InitialSchema.cs`)。
- 大多数 Controller 在 POST 路径手动赋 `CreatedAt = DateTimeOffset.UtcNow`,但 **`TenantUsersController` (User / Invitation) 与 `TenantRolesController` (Role) POST 漏赋**。
- EF mapping (`AppDbContext.OnModelCreating`) 没用 `ValueGeneratedOnAdd` / `HasDefaultValueSql`,DB 端 PG `DEFAULT now()` 也未配,所以 entity 漏赋值 = 进 DB 就是 `MinValue`。
- JSON 配置 `Program.cs:152-183` 没设 `DateTimeZoneHandling`(STJ 也没这个 API),`DateTimeOffset.MinValue` 直接序列化为 `0001-01-01T00:00:00.0000000+00:00`。

### 已有修复尝试

| Commit | 时间 | 内容 | 状态 |
|---|---|---|---|
| `4f06d05` | 2026-08-31 | `gen-shared.sh` 给 NSwag 输出注 `[JsonIgnore(WhenWritingDefault)]` 覆盖 `parentId` / `lastUsedAt` / `expiresAt` / `revokedAt` 4 个 nullable 字段 | **部分成功** — `createdAt` / `updatedAt` 在 OpenAPI 是 `required`,NSwag 生成 non-nullable `DateTimeOffset`,`?? default` 兜底照样写 MinValue |

### 调查步骤(按代价从小到大)

- [ ] 1. **必做**:`grep -n "CreatedAt\s*=\s*DateTimeOffset.UtcNow\|new DateTimeOffset" src/Controllers/Implementation/TenantUsersController.cs src/Controllers/Implementation/TenantRolesController.cs` 确认漏赋值点
- [ ] 2. **必做**:修 `TenantUsersController.cs:96-107` (UsersPost)、`117-125` (Invitations) 与 `TenantRolesController.cs:85-92` (RolesPost) 三处,补 `CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow`
- [ ] 3. 顺手补 `TenantRolesController.cs:108-119` (RolesPatch),PATCH 同步刷 `UpdatedAt = DateTimeOffset.UtcNow`
- [ ] 4. 跑 `dotnet test src/Saas.Identity.AspNetCore.csproj` 全绿
- [ ] 5. 跑 `python scripts/gate.py -p saas-identity-platform-aspnetcore` 全绿
- [ ] 6. 起 4 后端 + `CONTRACT_TARGETS=msw,aspnetcore,springboot npx vitest run tests/tenant-users-write.test.ts tests/tenant-roles-write.test.ts tests/tenant-api-keys-write.test.ts`,确认 I19 / I34 / I57 转绿

### 防御性兜底(可选,主修 #2 后可视情加)

主修完成后,**防御性双层兜底**避免后续新 Controller 再漏赋:

- [ ] **A. SaveChanges interceptor**:在 `AppDbContext` 重写 `SaveChangesAsync`,扫 `Added` / `Modified` 实体,**只在 `CreatedAt == default` 时** set `DateTimeOffset.UtcNow`(避免覆盖 controller 已显式赋的值)。理由:根除「漏赋值」类 bug,代价是一次 interceptor 模板。
- [ ] **B. `gen-shared.sh` 扩列**:把 `for FIELD in parentId lastUsedAt expiresAt revokedAt` 扩到 `parentId lastUsedAt expiresAt revokedAt createdAt updatedAt joinedAt grantedAt occurredAt consumedAt`。**注意**:这会让 DTO 在漏赋时 omit 字段而非返 `MinValue`,契约测试反而看不出来 — 所以 B 仅作「service 实兜底补 B 之前」的过渡,**不能取代 A**。
- [ ] **C. DB 层 DEFAULT**:EF `ValueGeneratedOnAdd().HasDefaultValueSql("now()")` + shared SQL `created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP`。**违反 ADR-0010「EF ↔ SQL diff=0」**,不推荐。

### 修复后回归

- [ ] contract-test I19 / I34 / I57 `normalize 后所有目标的成功响应字段一致` 转绿
- [ ] I15 / I71(列表端点)的 `items[*].createdAt` 不再触发「年份超出 [1970,2100]」
- [ ] 本机 prod-build smoke:启动 → POST user/role → GET 列表 → createdAt 是当前时刻(ISO 8601 毫秒 UTC)

### 推荐默认值(user 拍板 2026-09-01)

entity 字段如果需要兜底默认值,**不要用 `DateTimeOffset.MinValue` 或 `default`**,用 **Unix 纪元**:

```csharp
// C# DateTime / DateTimeOffset (NET 6+)
public static readonly DateTimeOffset CreatedAtDefault = DateTimeOffset.UnixEpoch;  // = 1970-01-01T00:00:00+00:00
// 或手写:
// public static readonly DateTime CreatedAtDefault = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
// DateTimeOffset.UnixEpoch 经 STJ round-trip 输出 "1970-01-01T00:00:00+00:00",
// contract-test assertTimestampShape [1970, 2100] 合法
```

**验证 `DateTimeOffset.UnixEpoch` 在 .NET 8 实际值**:`DateTimeOffset.UnixEpoch` = `1970-01-01T00:00:00.0000000+00:00`(不是 `0001-01-01`)。**用 `DateTimeOffset.UnixEpoch` 即可**,无须手写。

### 风险

合约测试 ADR-0015-amend 通过后,I19 / I34 / I57 会改用「格式断言」比较 4 后端时间戳格式(`…Z` 毫秒)。
**新断言下 `+00:00`(STJ DateTimeOffset round-trip 默认)仍是合法字符串长度,**但 `MinValue` 的年份 0001 仍然不在 `[2000, 2100]` 区间 — 所以**本 bug 仍会被合约侧抓到**,不能被盖住。

但 ADR-0015-amend Acceptance precondition 要求「springboot DateTime.MinValue 修完才能让 ADR Accepted」。
本仓 aspnetcore 这条同理:**ADR-0015-amend 跨两个后端共享 precondition**。

## 迭代方向

- (待补)

