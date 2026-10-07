# ADR-RateLimiting-点检查原语与三层限流分工

> **状态**：已批准（Oracle7 复审：APPROVE WITH REMAINING CONDITIONS (2)——RC1/RC2 已并入）
> **日期**：2026-10-07
> **关联**：ADR52（Utility 收纳准则"三不"）· ADR-RateLimiting-扩展边界与Web层接线 · D10D（V4.9.26 退役 IRateLimiter）· ADR89（UpdateWhereAsync 条件原子更新）· MFA README（跨实例 DB 频控候选）· 开发方案 `docs/RateLimiting/IRateLimitCheck抽象与三层限流分工-开发方案.md`（v0.3.0）

---

## 一、目的与目标

在 TKWF 生态建立**三层限流分工**中的"点检查原语"层：运行库（`TKWF.Utility`）提供 `IRateLimitCheck` 抽象 + 纯 BCL 内存默认实现（`MemoryRateLimitCheck`，上移自 MFA `MfaRateLimiter`）；扩展（`TKWF.Ext.RateLimiting`）提供数据库持久化 Provider（`SqlCountRateLimitCheck`，独立计数表 + ADR89 CAS）；与既有 Web 层中间件（RateLimiting 扩展）、Domain 层 `[RateLimit]` AOP 形成分工。目标读者应在 3 句话内明白：本 ADR 裁定"新增一个存储后端可插拔的点检查限流原语"，它不与 D10D 退役的算法抽象层冲突，落位 Utility 合规 ADR52，MFA 静态单例上移为可注入生命周期且安全语义不降级。

## 二、问题

### 问题现象
1. 仓库存在 **4 处互不共享抽象的频控实现**：AuthCenter `AuthLoginAttempt`/`SmsRecord` 表内 SQL COUNT 下推（`CountSentAsync`/`CountInWindowAsync`）、MFA `MfaRateLimiter`（`internal sealed` 内存滑动窗口 + **static readonly 进程级单例**）、Account `AccountLockout`（计数+绝对截止，属锁定非限流）、RateLimiting 扩展（纯 Web 层内存中间件）。零统一接口、零共享存储契约、六种窗口模型并存。
2. **MFA 静态单例的跨实例缺口已登记未收编**：`MfaRateLimiter` 承载于 Scoped 服务上的 static 单例，跨请求生效但**单实例语义**——多实例部署不跨实例共享；SMS 发码多实例 = 短信计费滥用真实风险（Oracle C8），README 已登记"跨实例 DB 频控"为 v0.2.0 候选但从未落地。
3. **官方类型无存储后端抽象**：Domain 层 `IRateLimitPolicyRegistry.Register(policyName, PartitionedRateLimiter<string>)` 接受 `System.Threading.RateLimiting` 的**具体类**（内存态），DB 持久化版无法实现该具体类——存储后端维度无表达通道。

### 触发场景
- MFA 验证尝试频控（`mfa:verify:{userId}:{method}`，5 次/5min）、短信发送频控（`mfa:sms:{userId}`，5 次/小时）——当前内存单实例，需跨实例正确性；
- 认证中心改密/找回频控（未来密码能力）、聚合逻辑内窗口计数——需服务内任意点的检查原语；
- 消费方多实例部署——需 DB/分布式后端。

### 现有方案的不足
- "让扩展层各自实现"已产生 4 处重复（零抽象），继续分散将固化石碎片；
- "复用 AuthCenter 审计表 COUNT"**不可行**：审计表是追加写审计轨迹（每行有独立审计价值），COUNT 方法是**只读**查询（`CountAsync(predicate)`），无原子递增能力；塞进通用计数会膨胀审计表、歧义 COUNT 语义；
- "用 VEntity（View SQL）当计数器"**结构性不可能**：`FreeSqlEntityDAC.GuardAgainstViewEntity()` 在全部 7 个写方法前置拒绝 `IDomainViewEntity`（`_TKWF/_Domain.Infrastructure/FreeSql/FreeSqlEntityDAC.cs` L64-66）——计数器必须写（原子递增），VEntity 只读；且 `tkwf-ventity-design` L144 明令"不为单实体聚合统计用 VEntity"、生产需 DBA 手动建视图。

## 三、使用场景

### 适用场景
- **服务内任意位置的点检查限流**（非方法级 AOP、非 HTTP 中间件）——MFA 验证/发送、改密/找回频控、聚合逻辑内计数；
- **需跨实例/DB 持久化的限流**——多实例部署的 MFA 短信计费防护、共享计数；
- **存储后端可插拔**——内存（默认/单实例）、DB（SqlCount/跨实例）、分布式（Redis/HybridCache，v0.2.0 候选）。

### 不适用边界
- **非窗口算法**（token bucket、concurrency）→ 不进入本契约，仍用官方 `PartitionedRateLimiter`（Domain AOP 层）；
- **方法级声明式 AOP 限流** → 既有 `[RateLimit]` + `FilterBuilder.AddRateLimit()`，本原语不替代；
- **HTTP 入口粗粒度限流** → 既有 RateLimiting 扩展 Web 层中间件；
- **需审计轨迹的场景**（登录尝试/短信发送——每行有独立审计价值）→ 审计表 COUNT（AuthCenter 现状保留），非本计数表；
- **VEntity/View SQL** → 不用于内部热路径计数器（只读 + 生产 DBA 依赖 + 读模型定位）。

## 四、选项

### 决策点 1：抽象落位——运行库（Utility）vs 扩展 Abstractions
- **A（选定）**：抽象 + 内存默认进 `TKWF.Utility`（`TKW.Framework.Utility.RateLimitChecks`）。理由：① ADR52"三不"合规（纯 `System.Collections.Concurrent`、零第三方、零反射、无 DI）；② 保持 MFA"零扩展间依赖"不变式（若放扩展 Abstractions，MFA 需引用扩展包，破坏依赖边界）；③ 对齐 `TKWF.Cryptography` 收纳实证（纯 BCL 进 Utility）。抽象放扩展层"不是死规则，按需"——此处为运行库原语，消费方是多扩展，应落 Utility。
- B：放 `TKWF.Ext.RateLimiting.Abstractions`——MFA/AuthCenter 需引用扩展包，耦合扩展版本线，违反 MFA 零依赖不变式。否决。

### 决策点 2：SqlCount 后端——独立计数表 vs 复用审计表 vs VEntity
- **A（选定）**：独立计数表 `RateLimitCounterEntity`（Key 唯一, Count, WindowStartUtc, WindowEndUtc，`[DomainGenerateCode]`）+ ADR89 `UpdateWhereAsync` CAS（`SET Count=Count+1 WHERE Key=@key AND WindowEndUtc>=@now AND Count<@max`，affected=1 成功 / =0 读行分辩；首插靠唯一约束捕获冲突重试）。理由：① 泛型契约表无关（复用审计表需 per-scenario 列映射适配，破坏 `IRateLimitCheck` 泛型）；② CAS 真原子（"append+COUNT" INSERT 后 COUNT 竞态可超限）；③ 跨实例正确（DB 行锁）；④ 决策规则：需审计→审计表 COUNT（AuthCenter 保留）；仅需限流→RateLimitCounter（MFA）。
- B：复用 AuthCenter `CountSentAsync`/`CountInWindowAsync`——只读审计查询，无递增能力，破坏泛型。否决。
- C：VEntity/View SQL——运行期写守卫拒绝，只读模型错位。否决（见 §二 现有方案不足）。

### 决策点 3：默认注册（RC1 排序裁定）
- **A（选定）**：`MFAExtensionInitializer` 注册 `TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>()` 作为 MFA 自有 fallback（短信计费防护"始终在"不降级为空）；**消费方须显式注册** `AddSingleton<IRateLimitCheck, SqlCountRateLimitCheck>()` 作为确定性路径（MFA 与 RateLimiting 扩展无依赖约束，TryAdd 首注册胜出下依赖扩展自动注册序不可靠）。
- B：依赖"RateLimiting 扩展自动预注册 SqlCount 让 TryAdd 胜出"——扩展执行序未定义，可能静默取 Memory。否决（UX 缺口）。

### 决策点 4：窗口语义（RC2）
- **A（选定）**：契约服务"窗口计数语义"，**算法为 Provider 内部实现**——Memory=sliding（时间戳队列，继承 MfaRateLimiter）、SqlCount=fixed（单窗口计数器，`WindowEndUtc` 过期重置）。Provider 切换行为差异（fixed 允许窗口边界双倍突发）文档化，MFA Memory→SqlCount 变更记入 MFA README。
- B：强制 SqlCount 也 sliding（逐事件时间戳/分段）——v0.1.0 过度设计。否决（v0.2.0 候选）。

## 五、决策

1. 新增 `IRateLimitCheck` 抽象 + `MemoryRateLimitCheck` 进 `TKWF.Utility`（ADR52 三不合规，零新增包）。
2. 新增 `SqlCountRateLimitCheck` 进 `TKWF.Ext.RateLimiting`（独立计数表 + ADR89 CAS）。
3. MFA `MfaRateLimiter` 删除，改 DI 注入 `IRateLimitCheck`；MFAExtensionInitializer 注册 Memory fallback；消费方显式注册决定 SqlCount。
4. 拒绝语义：点检查层返回 `bool + out remaining`（不抛异常），429 语义仅在调用方决定拒绝时由上层表达。
5. AuthCenter 登录保护（审计表 COUNT）**保留不迁移**（双目的正确）；决策规则写入使用指南。
6. 三层限流分工成文（Web 中间件 / Domain AOP / 点检查原语）。
7. **非 D10D 违规论证**：D10D V4.9.26 退役的是"选算法"抽象层（`IRateLimiter` 的 TokenBucket/SlidingWindow 选择），本抽象只服务"窗口计数语义 + 存储后端维度"，算法选择仍留 Provider 内部与官方 `PartitionedRateLimiter`——不复活算法抽象。
8. **孤儿键增长**（RC3）：`RateLimitCounter` 孤儿键无自然回收、增长无界——v0.1.0 已知限制，消费方限制键空间为有界标识（如 userId）；清理任务 v0.2.0。

## 六、后果

### 正面影响
- 消除 4 处频控零抽象碎片化的核心重复（MFA 上移、共享契约）；
- MFA 静态单例 → 可注入生命周期（对齐 DI004 纪律）+ 跨实例 DB 后端可用；
- 三层分工清晰：Web 粗粒度 / AOP 方法级 / 点检查服务内，各司其职不叠加；
- 零新框架原语需求（ADR89 已具备 CAS）。

### 负面影响与风险
- MFA 从 Memory（sliding）切 SqlCount（fixed）：窗口边界突发行为变化——MFA 验证低频可接受，记入 MFA README；
- 消费方若依赖"扩展自动注册序"选 SqlCount：可能静默取 Memory（RC1 已裁定显式注册为确定性路径）；
- `RateLimitCounter` 孤儿键增长无界（v0.1.0 限制，清理 v0.2.0）。

### 后续待办
- v0.2.0：Redis/HybridCache Provider（外部依赖变更须另立 ADR）；`RateLimitCounter` 清理任务；keyed 多 Provider 按调用点选择（E4 `ISymmetricKeyProvider` 先例）；`vw_RateLimitState` 监控视图（ops/可观测性，属另一扩展域）。

## 七、关联文档

- 开发方案：`docs/RateLimiting/IRateLimitCheck抽象与三层限流分工-开发方案.md`（v0.3.0）
- ADR52（Utility 收纳准则）、ADR-RateLimiting-扩展边界与Web层接线、D10D、ADR89（UpdateWhereAsync）、ADR90（领域自治）、ADR92（多实现集合守卫工厂）

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-10-07 | 起草 v1（Oracle7 复审 APPROVE WITH REMAINING CONDITIONS (2)） |
| 2026-10-07 | 并入 RC1（TryAdd 排序：消费方显式注册为确定性路径）、RC2（Provider 窗口语义差异文档化）、RC3（孤儿键清理计划入后果）——定稿 |

---
<!-- EOF -->
