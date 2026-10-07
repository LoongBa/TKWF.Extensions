# IRateLimitCheck 抽象与三层限流分工——开发方案

> **版本**：v0.3.0（Oracle7 复审终版——C1-C10 全通过 + RC1/RC2/RC3 已并入）
> **架构依据**：ADR52（Utility 收纳准则"三不"）· ADR-RateLimiting-扩展边界与Web层接线（v0.2.0 Redis 留白）· D10D（V4.9.26 退役自定义 IRateLimiter 教训）· MFA README（跨实例 DB 频控候选登记）· Domain RateLimiting Oracle W5 留白（拒绝语义规范化）
> **状态**：提议（Oracle7 复审：**APPROVE WITH REMAINING CONDITIONS (2)**——RC1/RC2 已并入，可进入分工实施）
> **前置**：主框架 `System.Threading.RateLimiting` 10.0.10（仅 Domain 引用）· MFA `MfaRateLimiter`（internal sealed，生产验证）· ADR89 `UpdateWhereAsync` 条件原子更新原语（`IEntityDAC` L73 / `DomainDataServiceBase` L249）
> **复用**：`MfaRateLimiter` 滑动窗口实现（内存默认原型）· `SmsRecord/AuthLoginAttempt` SQL COUNT 下推（DB Provider 语义参考）· `RateLimitException`（Core，已存在，429 语义）· ADR89 `UpdateWhereAsync`（SqlCount 原子性）

---

## 一、本版目标

**核心交付**：在 TKWF 生态建立**三层限流分工**中的"点检查原语"层——运行库（`TKWF.Utility`）提供 `IRateLimitCheck` 抽象 + 纯 BCL 内存默认实现；扩展（`TKWF.Ext.RateLimiting`）提供数据库持久化 Provider（`SqlCountRateLimitCheck`，独立计数表）；与既有 Web 层中间件、Domain 层 `[RateLimit]` AOP 形成分工，并将 MFA 的 `MfaRateLimiter` 静态单例上移为可注入生命周期。

**完成定义（DoD）**：
- [ ] `TKWF.Utility` 新增 `TKW.Framework.Utility.RateLimitChecks` 命名空间：`IRateLimitCheck` + `MemoryRateLimitCheck`（零 PackageReference 新增，ADR52 三不合规）
- [ ] `TKWF.Ext.RateLimiting` 新增 `SqlCountRateLimitCheck`（独立计数表 + ADR89 条件原子更新 TryAcquire，跨实例正确）
- [ ] MFA `MfaRateLimiter` 删除，`MfaService`/`SmsMfaMethod` 改 DI 注入 `IRateLimitCheck`；**MFAExtensionInitializer 注册 `MemoryRateLimitCheck` 作为扩展自有 fallback（TryAddSingleton）**——未启用 RateLimiting 扩展时频控**不降级为空**（安全语义保持：SMS 计费滥用防护始终在）
- [ ] 三层分工成文（Web 中间件 / Domain AOP / 点检查原语），使用指南补章节
- [ ] **编写 `ADR-RateLimiting-点检查原语与三层分工.md`**（AGENTS §4 三问必填：新框架抽象 + 新表 + 三层分工 = 关键设计取舍，须记录为何非 D10D 违规）
- [ ] slnx 构建 + 全量回归零失败；新增测试覆盖内存/DB 两 Provider + MFA 迁移 + SqlCount 并发原子性

> **Oracle7 评审结论（2026-10-07）**：APPROVE WITH CONDITIONS。C1（默认注册缺口，Blocker）→ 本版采纳 Option B（MFA 扩展自有 fallback 注册）；C2（原子性 TOCTOU，Blocker）→ 本版改用 ADR89 `UpdateWhereAsync` CAS；C3（缺 ADR，Blocker）→ 本版 DoD 新增 ADR 项；C4-C10（次要）→ 已并入 §五/§六 对应处。

---

## 二、范围

### 包含
- R1 运行库抽象：`IRateLimitCheck` 契约 + `MemoryRateLimitCheck` 默认实现（`MfaRateLimiter` 上移改造，`internal`→`public`，去 MFA 语义）
- R2 DB Provider：`SqlCountRateLimitCheck`（扩展侧，独立计数表 `RateLimitCounter`，**ADR89 `UpdateWhereAsync` 条件原子更新**实现 TryAcquire）
- R3 MFA 迁移：`MfaService.VerifyLimiter` / `SmsMfaMethod.SendLimiter` 两处静态单例 → DI 注入 `IRateLimitCheck`；**MFAExtensionInitializer 注册 `MemoryRateLimitCheck` fallback（TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>）**——RateLimiting 扩展启用并预注册 `SqlCountRateLimitCheck` 时 TryAdd 让后者胜出
- R4 分工成文：三层职责文档化（使用指南 §新增），拒绝语义统一为 `RateLimitException`（429）
- R5 ADR：编写 `ADR-RateLimiting-点检查原语与三层分工.md`（三问必填：目的/问题/使用场景 + 非 D10D 违规论证 + Utility 落位论证 + 独立计数表论证）

### 不包含
- ❌ Redis/分布式缓存 Provider——v0.2.0 候选（依赖 `StackExchange.Redis`/HybridCache，**外部依赖变更须另立 ADR**）
- ❌ 改造 Domain 层 `[RateLimit]` AOP / `EnforceAsync` 拒绝语义（Oracle W5 留白登记，`RateLimitException` 已存在；仅文档化"抽象上提时一并规范化"为后续项）
- ❌ 改动 Web 层 RateLimiting 扩展现有接线（`RateLimitingWebExtension` 保留）
- ❌ AuthCenter 登录保护（`AuthLoginAttempt`/`SmsRecord` 审计表计数）迁移——**保留审计表兼计数双目的**（正确设计，不强行并入通用计数表）
- ❌ 新建 `IRateLimitCheck` 独立 NuGet 包——抽象 + 内存实现进 Utility（无其它依赖不独立包，ADR52 决策 1）
- ❌ **已知限制（v0.1.0 明示，Oracle7 C6/C8）**：① `RateLimitCounter` 表孤儿键（不再命中的 key）无自然回收，**增长无界**——清理任务 v0.2.0；消费方应限制键空间为有界标识（如 userId）直至届时；② **单 `IRateLimitCheck` 注册全局生效**——消费方想要"verify 用内存、sms-send 用 DB"的按调用点 Provider 选择，需等 v0.2.0 keyed 形态（先例：E4 `ISymmetricKeyProvider`）

---

## 三、设计依据

| 文档 | 版本/状态 | 关键裁定 |
|---|---|---|
| ADR52-运行时工具库定位与命名 | v4.9.91 已批准 | "三不"收纳准则：不引第三方包 / 不引反射 / 常用可复用 / 纯算法无 DI → 进 Utility；需要 DI/持久化/生命周期 → 扩展模块 |
| ADR-RateLimiting-扩展边界与Web层接线 | 已批准 | 扩展聚焦 Web 层，不重建 Domain 层；**Redis 分布式 v0.2.0 候选（需引第三方包，须另立 ADR）** |
| D10D-限流架构-设计方案 | V4.9.26 执行 | 退役自定义 `IRateLimiter` 抽象层（"跟生态，减少自定义抽象"）——**本方案不复活算法抽象，官方 `PartitionedRateLimiter` 继续用于 AOP 层**；补的是官方未覆盖的"存储后端维度" |
| MFA README §后续演进 | V0.2.0 | 已登记「跨实例 DB 频控（对齐 SecurityLog 清理范式）」候选——本方案正式收编 |
| Domain RateLimiting `EnforceAsync` 注释 | Oracle W5 | 限流当前仅认证场景用 `AuthenticationException`（401）；「若未来限流扩展至非认证场景，应引入专用 `RateLimitException` 再评估」——`RateLimitException`（429）已存在于 `TKW.Framework.Core.RateLimiting`，本方案的点检查层直接以 `bool + RetryAfter + Remaining` 返回值表达，不抛异常，规避此留白 |

---

## 四、任务拆解

| 任务 | 描述 | 工作量 |
|---|---|---|
| R1 | `TKWF.Utility` 新增 `RateLimitChecks/`：`IRateLimitCheck` + `MemoryRateLimitCheck`（上移改造 `MfaRateLimiter`，补齐 key 规范化约定） | M |
| R2 | `TKWF.Ext.RateLimiting` 新增 `SqlCountRateLimitCheck`：实体 `RateLimitCounter`（Key 唯一, Count, WindowStartUtc, WindowEndUtc，`[DomainGenerateCode]`）+ **TryAcquire 经 ADR89 `UpdateWhereAsync` CAS**（`SET Count=Count+1 WHERE Key=@key AND WindowEndUtc>=@now AND Count<@max`；affected=1 成功 / =0 读行分辩窗口过期或超限；首插靠 Key 唯一约束捕获冲突重试）+ 惰性淘汰 | M |
| R3 | MFA 迁移：删 `MfaRateLimiter`，`MfaService`/`SmsMfaMethod` 改 `GetOptionalService<IRateLimitCheck>()` 接线型消费；**MFAExtensionInitializer `TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>()` 注册 fallback**（未启用 RateLimiting 扩展 → 用内存默认，频控不消失）；key 格式按规范重写（内存态重启即重置，无迁移成本） | S |
| R4 | 三层分工成文：使用指南 §三层限流分工（Web/Domain/点检查各司其职表）+ 拒绝语义（429 `RateLimitException`）+ 审计表计数 vs 通用计数表**决策规则** | S |
| R5 | `ADR-RateLimiting-点检查原语与三层分工.md`（三问必填：目的与目标/问题/使用场景；含非 D10D 违规论证、Utility 落位论证、独立计数表论证、单 Provider v0.1.0 限制与 keyed v0.2.0 候选） | S |

---

## 五、技术方案

### 模块结构

```
TKWF.Utility（主框架，零依赖新增）
└── TKW.Framework.Utility.RateLimitChecks/
    ├── IRateLimitCheck.cs            # 抽象契约
    └── MemoryRateLimitCheck.cs       # 内存默认（MfaRateLimiter 上移改造）

TKWF.Ext.RateLimiting（扩展）
└── TKWF.Ext.RateLimiting/
    ├── SqlCountRateLimitCheck.cs     # DB 持久化 Provider（partial DataService 手写业务方法）
    ├── Entities/RateLimitCounterEntity.cs   # 计数表实体（[DomainGenerateCode]，partial）
    └── DataServices/RateLimitCounterEntityDataService.cs  # 生成基座 + 手写 TryAcquire（ADR89 CAS）
```

### 接口定义

```csharp
namespace TKW.Framework.Utility.RateLimitChecks;

/// 点检查限流原语——按 key 在窗口内计数，超限拒绝。
/// 与官方 System.Threading.RateLimiting（Domain AOP 层用）区分：本契约是"存储后端可插拔的检查原语"，
/// 支持内存/DB/分布式 Provider。抽象在运行库、Provider 按需在扩展（ADR52 分层）。
public interface IRateLimitCheck
{
    /// 判定并计入一次尝试。窗口内已计数 ≥ maxAttempts → false（remaining=0，调用方经 GetRetryAfter 取可重试时间）。
    /// 入参守卫：key 空/白、maxAttempts ≤ 0、window ≤ Zero → false（fail-closed，不静默放行）。
    bool TryAcquire(string key, int maxAttempts, TimeSpan window, out int remaining);

    /// 窗口满时下一次尝试可行的剩余等待时长；未满/键不存在 → TimeSpan.Zero。
    TimeSpan GetRetryAfter(string key, TimeSpan window);

    /// 当前剩余额度（只读不计数）；键不存在 → maxAttempts。
    int GetRemaining(string key, int maxAttempts, TimeSpan window);
}
```

```csharp
// MemoryRateLimitCheck —— MfaRateLimiter 上移改造（核心逻辑不变）
namespace TKW.Framework.Utility.RateLimitChecks;

public sealed class MemoryRateLimitCheck : IRateLimitCheck
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new();
    // 滑动窗口：读取时惰性淘汰（Peek <= now - window → Dequeue）；per-key 细粒度 lock；
    // UTC 基准；空队列 TryRemove 回收防无界累积；全部语义继承 MfaRateLimiter（生产已验证）。
}
```

```csharp
// SqlCountRateLimitCheck —— DB 持久化 Provider（跨实例正确）
namespace TKWF.Ext.RateLimiting;

public sealed class SqlCountRateLimitCheck : IRateLimitCheck
{
    // 独立计数表 RateLimitCounterEntity（[DomainGenerateCode]，partial DataService）：
    //   Id, Key(唯一约束 UX_RateLimitCounter_Key), Count, WindowStartUtc, WindowEndUtc
    // TryAcquire 原子语义（红线合规——经 DataService + ADR89 UpdateWhereAsync CAS，非读-改-写 TOCTOU）：
    //   A. 首插：Insert(Key, Count=1, WindowStartUtc=now, WindowEndUtc=now+window)
    //        → 唯一约束冲突（并发首插）→ 捕获后走 B
    //   B. 窗口内递增：UpdateWhereAsync(Key == @key && WindowEndUtc >= @now && Count < @max)
    //        → Update().Set(Count = Count + 1)。affected==1 → 成功；affected==0 → 读行分辩：窗口过期 → 重置
    //        Count=1, WindowEndUtc=now+window；Count 已达 max → false（超限）
    //   C. 窗口过期重置：UpdateWhereAsync(Key == @key && WindowEndUtc < @now)
    //        → Update().Set(Count = 1, WindowEndUtc = now + window)
    // 惰性淘汰：过期行随下一次命中该 Key 时重置（无后台清理，对齐 MfaRateLimiter 哲学）；
    // ⚠️ 已知限制（Oracle7 C6）：孤儿键（不再命中）无自然回收，表增长无界——清理任务 v0.2.0；
    //    消费方应限制键空间为有界标识（如 userId）直至届时。
    // GetRetryAfter/GetRemaining：读行计算（WindowEndUtc - now / max - Count）。
}
```

### 三层限流分工（R4 成文核心）

| 层 | 载体 | 粒度 | 用途 | 拒绝语义 |
|---|---|---|---|---|
| **Web 层** | `RateLimitingWebExtension`（ASP.NET Core `AddRateLimiter` 中间件） | IP/端点粗粒度 | 登录 IP 防爆、OpenAPI 端局限流 | HTTP 429 + `Retry-After` |
| **Domain AOP** | `FilterBuilder.AddRateLimit()` + `[RateLimit("policy")]` + 官方 `PartitionedRateLimiter` | 用户/方法级 AOP | 服务方法级用户频控 | `RateLimitException`（429） |
| **点检查原语**（本方案新增） | `IRateLimitCheck`（运行库抽象 + 内存默认）+ 扩展 Provider（DB） | 服务内任意点 | MFA 验证尝试、短信发送、改密/找回频控、聚合逻辑内检查 | `bool + RetryAfter + Remaining` 返回值（不抛异常，调用方自决） |

> **分工原则**：Web 层管"谁在打 HTTP 入口"，Domain AOP 管"方法级策略限流"，点检查原语管"服务内任意位置的窗口计数"——三层互补不重叠；同一路径**不要**叠加相同语义（既有指南警示：配额独立会叠加计数）。

> **D10D 边界说明（Oracle7 C4 + RC2 修正）**：`IRateLimitCheck` 契约服务**窗口计数语义**——Memory Provider 为 sliding window（继承 `MfaRateLimiter` 时间戳队列），SqlCount Provider 为 fixed window（单窗口计数器，`WindowEndUtc` 过期重置）。**算法选择是 Provider 内部实现，非契约级**（符合 D10D"算法不抽象化"精神）；非窗口算法（token bucket、concurrency）**不进入**本契约——仍留在官方 `PartitionedRateLimiter`（Domain AOP 层）。**本抽象不是 D10D V4.9.26 退役 `IRateLimiter` 的复活**（退役的是"选算法"抽象层），补的是官方类型无法表达的"存储后端维度"。⚠️ **Provider 切换行为差异**：MFA 从 Memory（sliding）切到 SqlCount（fixed）时，窗口边界爆发行为变化（fixed 允许边界双倍突发）——MFA 验证低频可接受，记入 MFA README。此论证写入 R5 ADR。

### ADR52 合规论证（R1 关键）

| ADR52"三不"条款 | `IRateLimitCheck` + `MemoryRateLimitCheck` 满足度 |
|---|---|
| 不引入第三方包 | ✅ 纯 `System.Collections.Concurrent` + `System`，零 PackageReference 新增 |
| 不引入反射/动态编译 | ✅ 无反射（AOT 友好） |
| 常用/可复用 | ✅ 限流是横切通用能力（MFA/AuthCenter/未来扩展均消费） |
| 纯算法/纯工具（无 DI） | ✅ 抽象接口 + 内存实现均无 DI 依赖 |
| 需要 DI/持久化/生命周期 → 扩展模块 | ✅ `SqlCountRateLimitCheck`（持久化 + DataService）留 `TKWF.Ext.RateLimiting` |

> **对照收纳实证**：与 `TKWF.Cryptography` 并入 Utility 同型（纯 BCL、无 DI、零第三方）——R1 合规；`SqlCountRateLimitCheck` 需要持久化/DI → 按"三不"排除条款归属扩展——R2 合规。**不新增独立包**（无其它依赖，对齐 ADR52 决策 1）。

### 设计决策点（Oracle7 已评审定案）

1. **窗口参数形态**：抽象层统一 `TimeSpan window`（与内存原型 `MfaRateLimiter` 一致，调用方心智负担最低）；DB Provider 内部转 `WindowEndUtc = now + window`（绝对边界）。**定案（Oracle7 通过）**——不采用 `DateTime fromUtc`（`SmsRecord`/`AuthLoginAttempt` 是审计查询风格，与点检查计数语义不同）。
2. **拒绝语义**：点检查层**不抛异常**（`bool + out remaining`），与 Domain AOP 的"抛 `RateLimitException`"区分——因为点检查是"服务内自决"，调用方可能选择降级而非拒绝。**定案（Oracle7 通过）**。
3. **key 规范化**：抽象层定义键格式 `{policy}:{partitionBy}:{subject}`（对齐 Domain AOP `rl:{policy}:user:{userId}` 风格）；MFA 迁移后 `mfa:verify:{userId}:{method}` → `mfa:verify:{policy}:{userId}:{method}` 形态。**定案（Oracle7 通过）**——⚠️ 内存态重启即重置，key 格式变更无升级迁移成本（Oracle7 C10）。
4. **AuthCenter 登录保护不迁移**：`AuthLoginAttempt`/`SmsRecord` 审计表兼计数（双目的正确）**保留**；本方案仅新增通用计数表供"纯频控"场景（MFA 跨实例、找回/改密频控）。**定案（Oracle7 通过）**——补充**决策规则（Oracle7 C5）**：`需审计轨迹（登录尝试/短信发送——每行有独立审计价值）→ 审计表 COUNT`；`仅需限流、单次尝试无审计价值（MFA 验证、改密频控）→ RateLimitCounter`。未来贡献者按此规则选型，不猜测。
5. **默认注册缺口（Oracle7 C1，Blocker，已定案）+ TryAdd 排序（RC1）**：`MemoryRateLimitCheck` 是提供类而非 DI 注册。**定案（Option B）**——`MFAExtensionInitializer.ConfigureServices` 注册 `TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>()` 作为扩展自有 fallback；**MFA 短信路径的"始终在"安全语义（SMS 计费滥用防护）保持，不降级为空**。⚠️ **RC1 排序裁定（Oracle7）**：MFA 与 RateLimiting 扩展无依赖约束，TryAddSingleton（首注册胜出）下"SqlCount 胜出"依赖注册顺序——**消费方须显式注册 `IRateLimitCheck`（`AddSingleton<IRateLimitCheck, SqlCountRateLimitCheck>()`）作为确定性路径**；依赖"扩展自动注册序"不可靠。此机制写入 R5 ADR。
6. **原子性（Oracle7 C2，Blocker）**：见 §五 `SqlCountRateLimitCheck`——经 ADR89 `UpdateWhereAsync` CAS（`SET Count=Count+1 WHERE Key=@key AND WindowEndUtc>=@now AND Count<@max`），非读-改-写 TOCTOU。**定案**。
7. **SG1 实体声明（Oracle7 C7）**：`RateLimitCounterEntity` 标 `[DomainGenerateCode]`（partial `RateLimitCounterEntityDataService`）——自动 CRUD 供读，手写业务方法 `TryAcquire`（经 `InternalUpdateWhereAsync`）。**定案**——模块结构已更新。
8. **单 Provider vs keyed（Oracle7 C8）**：v0.1.0 单 `IRateLimitCheck` 注册全局生效（MFA 两限流器不同 key/参数共用）；按调用点 Provider 选择（verify 内存 / sms-send DB）**v0.2.0 keyed 形态**（先例 E4 `ISymmetricKeyProvider`）。**定案**——已列入 §二 已知限制。

---

## 六、验证清单

- [ ] ADR52 三不逐项自检通过（R1：无第三方包/无反射/纯算法无 DI；R2：持久化归扩展）
- [ ] `MemoryRateLimitCheck` 与 `MfaRateLimiter` 行为等价（原 MFA 测试迁移通过：验证尝试 5 次/5min、发送 5 次/小时、RetryAfter 推导、无界累积回收）
- [ ] `SqlCountRateLimitCheck` 原子性（Oracle7 C9 诚实声明）：**进程内并发 Task 测试**——两并发 `TryAcquire` 同 key 同窗口 → 恰一成功一失败（验证 ADR89 `UpdateWhereAsync` CAS 逻辑）；**跨实例正确性属 ORM/DB 契约（`UpdateWhereAsync` 映射真实 `UPDATE ... WHERE` SQL + 事务隔离），测试宿主（SQLite 内存单进程）无法证明跨进程隔离——此为 ORM 层责任，非测试可验证属性**
- [ ] MFA 迁移后：**未启用 RateLimiting 扩展 → MFA fallback `MemoryRateLimitCheck` 生效（频控不消失，安全语义保持）**；启用且注册 `SqlCountRateLimitCheck` → 后者胜出，行为与迁移前一致
- [ ] key 规范化：MFA 两限流器键按 `{policy}:{partitionBy}:{subject}` 重写；内存态重启即重置，零迁移成本
- [ ] 三层分工文档：Web/Domain/点检查职责表 + 拒绝语义（429）+ 审计表 vs 计数表决策规则成文
- [ ] `ADR-RateLimiting-点检查原语与三层分工.md` 三问完备 + 非 D10D 违规论证 + Utility 落位论证 + 独立计数表论证（含 VEntity 排除的写守卫证据）+ **RC1 TryAdd 排序裁定（消费方显式注册为确定性路径）+ RC2 Provider 窗口语义差异（Memory=sliding / SqlCount=fixed）+ RC3 孤儿键清理计划（v0.2.0，对齐 ADR89 §六 后果先例）**
- [ ] slnx 构建 0 错误 + 全量回归零失败（Domain AOP `RateLimitFilterAttribute` 现有断言保持——本方案零改动该层）

---
<!-- EOF -->
