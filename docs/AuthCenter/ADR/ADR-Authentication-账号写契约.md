# ADR-Authentication-账号写契约

## 状态

活跃

> 本 ADR 为永久架构决策记录，不可删除。如后续决策被推翻（如管理面 API 收敛写面/契约拆包），须在本 ADR 标注「已废弃」并引用新 ADR，而非删除本文件。

## 一、目的与目标

暴露认证中心**账号写契约** `IAuthAccountService`（`CreateAsync`/`UpdateAsync`/`IncrementTokenVersionAsync`/`GetByUIdAsync`，委托 `AuthAccountEntityDataService`，`TryAddScoped` 注册），使消费端（DMP-Lite 渐进替换路径）可创建/维护平台管理员**影子 AuthAccount**——解除 v0.2.0 方案 C1「写路径不暴露」对 DMP P1 令牌替换的阻塞。读者应在 3 句话内明白：`ITokenService.RefreshTokenAsync` 强依赖 AuthAccount 记录（不存在/禁用 → `ACCOUNT_NOT_FOUND`；TokenVersion 不匹配 → `REFRESH_STALE`），而扩展公开面此前仅只读 `IAuthAccountQueryService` → 消费端无公开路径建影子账号；本 ADR 裁定新增公开写契约 `IAuthAccountService`（public 接口 + internal sealed 实现 + TryAddScoped——对齐 `IAuthAccountQueryService`/`IdentityPasswordManager` 先例），**拒绝** DataService 改 public；`AdminDeleteAsync` 不暴露（与管理 API 一起留后续迭代）。

## 二、问题

### 问题现象

- **扩展公开面仅只读**：v0.2.0 仅暴露 `IAuthAccountQueryService`（4 只读方法，方案 C1 裁定「写路径 Create/Update/AdminDelete 不暴露——内部 TokenService 职责」）；唯一写路径 `AuthAccountEntityDataService`（`CreateAsync`/`UpdateAsync`/`IncrementTokenVersionAsync`/`AdminDeleteAsync`）为 **internal sealed partial**（.g.cs 分部同为 internal）。
- **消费端无法引用写路径**：SG1 `AddConstructibleDataService` 自动注册键是 internal 类型——消费端**编译期无法 `typeof` 引用**；`User.Use<T>()` 仅泛型且约束 `IDomainService`（`IDomainUser.cs` L41），且不经接口注册的内部类型仍无法经 DI 解析。→ 消费端**无任何公开路径**创建账号。
- **DMP 渐进替换被阻塞**：DMP-Lite v0.9.9 平台管理员 `PlatformAdmin` 留本地表（登录角色经 `IAuthorizationMapper` 本地映射），但扩展 `TokenService.RefreshTokenAsync`（L196-200）强依赖 AuthAccount（不存在/禁用 → `ACCOUNT_NOT_FOUND`；TokenVersion 不匹配 → `REFRESH_STALE`）——平台管理员必须存在 AuthAccount **影子记录**（`UId = PlatformAdmin.UId`，`IsEnabled=true`），否则 `/auth/refresh` 切扩展必报 `ACCOUNT_NOT_FOUND`。

### 触发场景

- DMP 登录端点需 **upsert 影子 AuthAccount**（`UId = PlatformAdmin.UId`，`Phone` 可空——唯一索引 NULL 放行）→ 切扩展 TokenService → `/auth/refresh` rotation 回归。
- 平台管理员改密后需 **bump 影子账号 TokenVersion** → 旧 refresh 失效（`REFRESH_STALE`）——否则改密后旧 refresh 仍可续期（安全缺口）。
- 平台管理员停用/回用 → 更新 `IsEnabled`（禁用后刷新报 `ACCOUNT_NOT_FOUND`）。

### 现有方案不足

- **只读契约不够**：`IAuthAccountQueryService` 只解决查询，无写路径——影子账号无法创建/更新。
- **DataService 改 public（选项 ②）**：① .g.cs 分部访问性必须与手写分部匹配——需改 xCodeGen 模板 + 重生成，管线摩擦；② 暴露整个 SG1 基座 CRUD 面（含 `EntityCreateBatchAsync` 等原子转发 + `AdminDeleteAsync`）——最小权限违反，消费端可绕过契约层；③ 无先例。
- **消费端留本地双轨**：DMP 继续自有 TokenService 双轨——令牌体系两套并存，P1 替换暂停（过渡成本持续）。

## 三、使用场景

### 适用场景

- DMP-Lite 渐进替换路径：登录端点 upsert 影子 AuthAccount（`UId=PlatformAdmin.UId`，`IsEnabled=true`，Phone 可空）→ 切扩展 TokenService → `/auth/refresh` rotation 回归 → 删自有 TokenService。
- 装配实例/其他扩展维护 AuthAccount 生命周期（建/改/失效）——经公开契约（红线合规委托 DataService）。
- 密码/绑定变更后失效旧 refresh：`IncrementTokenVersionAsync(uid)`（DMP PlatformAdmin 改密 → bump 影子账号 → 旧 refresh `REFRESH_STALE`）。

### 不适用边界

- **`AdminDeleteAsync` 不暴露**：破坏性操作，影子账号生命周期不需要——与管理端 API（批量撤销/黑名单清理）同一后续迭代。
- **写契约不合并进 `IAuthAccountQueryService`**：读/写职责分离（消费语境不同；合并 = 查询面被迫携带写方法）。
- **影子账号不填 `PasswordHash`**：DMP 登录验证走本地 `PlatformAdmin`，AuthAccount 仅作令牌刷新锚点（账号存在性 + IsEnabled + TokenVersion）——密码 Provider 尚属扩展点未启用，写契约不预设密码写语义（`PasswordHash` 仍可经 `UpdateAsync` 整实体更新，属消费方自决）。
- **`GetByUIdAsync` 在写契约中的定位**：dup 于 `IAuthAccountQueryService`（两契约委托同一 internal DataService，无状态风险）——为 DMP upsert 流单注入便利而设，非职责越界。

## 四、选项

### 选项 ①：公开契约 `IAuthAccountService`（选定）

- 描述：public 接口（4 方法含 `GetByUIdAsync`）+ `AuthAccountService` internal sealed 委托 `AuthAccountEntityDataService` + Initializer `TryAddScoped` 注册。
- 优点：零破坏增量（只增公开面）；对齐 `IAuthAccountQueryService` + `IdentityPasswordManager`（public sealed + TryAddScoped）先例；红线合规（委托 DataService 零 IFreeSql/IEntityDAC）；最小暴露（仅 4 方法，AdminDelete 不外放）；消费端构造注入/DI 解析（public 契约 typeof 可引用）。
- 缺点：写面公开后需文档边界防误用（管理面能力后续收敛）；与查询契约并存需明确读/写职责。

### 选项 ②：`AuthAccountEntityDataService` 改 public

- 描述：可见性放宽——手写分部 + .g.cs 生成分部均改 public。
- 缺点：.**g.cs 分部**访问性匹配要求（改模板 + 重生成，管线摩擦，重生成还原风险）；② 暴露整个 SG1 基座 CRUD 面（含原子转发/AdminDelete）——最小权限违反；消费端直拿 DataService 有触碰 `IEntityDAC` 面风险（红线边界模糊）；③ 无先例——仓库惯例是 public 契约 + internal 实现。

### 选项 ③：不实施（DMP 维持双轨）

- 描述：写契约与后续管理端 API 一并规划 v0.3.0。
- 缺点：DMP P1 令牌替换继续暂停（双轨过渡成本持有）；`/auth/refresh` 切扩展必报 `ACCOUNT_NOT_FOUND` 的阻塞不解除——推迟交付无收益（契约零破坏增量，成本低）。

## 五、决策

选定：**选项 ①——公开契约 `IAuthAccountService`（Create/Update/IncrementTokenVersion/GetByUId，委托 DataService，TryAddScoped）**。

理由：
1. **解除 DMP 阻塞**：消费端经公开契约 upsert 影子 AuthAccount → 扩展 TokenService refresh rotation 闭环 → P1 令牌替换解锁（DMP OI7 转移单，`v0.9.9-认证扩展-OI7-转框架组.md`）。
2. **先例一致**：public 契约 + internal sealed 实现 + TryAddScoped——本体 `IAuthAccountQueryService`（v0.2.0）+ 跨扩展 `IdentityPasswordManager` 完全同构；`AuthAccountService` 与 `AuthAccountQueryService` 同文件模式同风格。
3. **红线合规**：零 IFreeSql/零 IEntityDAC 直注入（全委托 `AuthAccountEntityDataService`）。
4. **最小面**：`AdminDeleteAsync`/批量撤销/黑名单清理留管理 API 迭代；写契约只覆盖 DMP 影子账号真实需要（Create/Update/失效）。
5. **C1 反转显式留档**：v0.2.0 方案 C1「写路径不暴露」裁定因真实消费需求（DMP 渐进替换）反转——本 ADR 为反转依据，非静默偏离。
6. `GetByUIdAsync` 纳入写契约（dup 查询契约）：DMP upsert 流单注入便利，两契约委托同一 internal DataService 无状态风险。

## 六、后果

### 正面影响

- DMP P1 令牌替换解锁（影子账号可建/可维护/可失效）；`/auth/refresh` 不再必报 ACCOUNT_NOT_FOUND。
- 零破坏增量（只增公开面，既有 8 面 + 查询契约消费方零迁移）；红线合规；最小暴露。
- 契约形态先例再充实：query/write 双契约 + 影子账号模式（Phone 可空影子记录）后续管理面可复用。

### 负面影响 / 风险

- 写面公开需文档边界（使用指南标注：写契约面向装配/迁移场景，业务主写路径仍归扩展内部——防误用/滥建账号）。
- `UpdateAsync` 整实体更新可触及 `PasswordHash`/`IsEnabled`/`TokenVersion`——属消费方（数据属主语义）自决，契约不设字段级门控（v0.1.0 方案偏离③ 同源：数据中心化信任消费方）。
- 与查询契约并存（`GetByUIdAsync` dup）——若后续治理要求唯一读源，可推动查询契约逆引用（YAGNI 下不预做）。

### 后续待办

- 管理端 API（账号/凭证管理端点——含 `AdminDeleteAsync` 收敛入口）随 v0.3.0 规划。
- 原子条件 UPDATE 安全迭代（`IncrementTokenVersionAsync` read-modify-write，oracle3 C3 既有待办——独立 v0.2.x 跟踪，不受本 ADR 影响）。
- DMP 迁移验证清单（影子账号 upsert → 切 TokenService → refresh 回归 → 删自有 TokenService）写入使用指南。

## 七、关联文档

- 转移单：`DMP-Lite/docs/02-迭代开发/V0.x/v0.9.9-认证扩展-OI7-转框架组.md`（DMP 侧提出，2026-10-01 转达）
- 方案 C1 反转源：`docs/框架实战教学/05-倒推优化-认证中心-查询契约与UserCenter承接-开发方案.md`（§四 C1「写路径不暴露」/ oracle3 C6 两契约边界）
- 契约形态先例：`ADR-Authentication-UserCenter契约承接.md`（public sealed + TryAddScoped 形态）；`IAuthAccountQueryService`（v0.2.0 查询契约）；`IdentityPasswordManager`（跨扩展契约实现）
- 机制约束：`IDomainUser.Use<T>()` 仅泛型（`IDomainUser.cs` L41）；SG1 `AddConstructibleDataService`（ADR61）；`TokenService.cs` L196-200（RefreshTokenAsync 账号依赖）

## 变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-10-01 | 初始版本——DMP OI7 转达缺口闭环：公开写契约 `IAuthAccountService`（Create/Update/IncrementTokenVersion/GetByUId，internal sealed 委托 DataService + TryAddScoped）；v0.2.0 方案 C1「写路径不暴露」反转留档（DMP 渐进替换真实需求）；拒绝 DataService 改 public（.g.cs 分部管线 + 最小权限）；AdminDelete 留管理 API 迭代 |