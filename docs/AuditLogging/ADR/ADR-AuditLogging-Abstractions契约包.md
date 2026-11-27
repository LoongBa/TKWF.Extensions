# ADR-AuditLogging-Abstractions契约包

> **版本**：v0.2
> **状态**：**已通过 Oracle5 评审（PASS WITH CONDITIONS，E1–E10 已闭环）**；待实施
> **关联**：`docs/AuditLogging/审计三面分层与业务审计-开发方案.md` §四/§八；《设计原则手册》P9（依赖新增须 ADR）/P13（分层单向）；`设计原则/扩展模块.md`（Abstractions 依赖倒置 ADR48 D7）；先例 `SecurityLog.Abstractions`、`Federation.Abstractions`、`Permissions.Abstractions`
> **日期**：2026-11-27

---

## 目的与目标

为审计能力新建 `TKWF.Ext.AuditLogging.Abstractions` 契约包，把业务审计门面、脱敏/失败/访问策略、事件载荷、查询契约与 Options 收敛为**消费方唯一编译期依赖**；实现（实体/DataService/门面实现/过滤器接线）留在 `TKWF.Ext.AuditLogging` 主包。目标是让消费方"只引契约即可用业务审计与信号"，避免直引实现主包（L2 门控 `TKWF0022`），并对齐仓库既有契约包先例。读者 3 句话内明白：AuditLogging 目前没有 Abstractions 包（框架 Core 提供了 `IAuditLogStore` 但业务审计/策略/事件需要新契约）；新包是 ADR48 D7 的正规落点，也是 P9 要求走 ADR 的"新增项目"。

## 问题

### 问题现象

1. `AuditLogging` 现有契约中，`IAuditLogStore`/`AuditLogEntry`/`AuditLogFilterAttribute` 由**框架 Core** 定义（不可本仓改）；而查询/分析门面（`IAuditLogQueryService`/`IAuditLogAnalyticsService`）定义在**主包**内——消费方要用就得直引实现主包。
2. 本方案新增的 `IBusinessAuditWriter`/`IAuditMaskingPolicy`/`IAuditFailureHook`/`IAuditAccessPolicy`/事件载荷/`IBusinessAuditQueryService` 若留在主包，将迫使所有消费方直引实现（违 L2/ADR48 D7）。
3. 跨扩展复用：`SecurityLog.Abstractions` 需复用同一失败信号契约（`IAuditFailureHook`），若无中立契约包会形成实现间耦合（P13 逆流）。

### 触发场景

- 消费方（DMP Platform / Merchant）引用业务审计与失败信号。
- SecurityLog 复用失败信号契约。
- 未来其他扩展（如 FeatureManagement 审计联动）消费审计事件。

### 现有方案的不足

- **留在主包**：消费方直引实现 → 违反依赖倒置（TKWF0022）；实现升级牵动所有消费方。
- **放到框架 Core**：本仓不可改框架；且业务审计/策略非框架机制，不应上提 Core（P13）。
- **不建包、各消费方自建契约**：口径漂移（P2）、系统帧写法各异。

## 使用场景

| 场景 | 使用方式 |
|---|---|
| 消费方记录业务审计 | 引 `AuditLogging.Abstractions`，用 `IBusinessAuditWriter` + `BusinessAuditEntry` |
| 自定义脱敏 | 实现 `IAuditMaskingPolicy` 并 `TryAdd` 替换 |
| 失败告警 | 实现 `IAuditFailureHook` |
| 订阅审计事件 | 实现 `ILocalEventHandler<AuditLogPersisted>`（附 `[DomainEventHandler]`） |
| SecurityLog 复用失败信号 | SecurityLog.Abstractions → AuditLogging.Abstractions（单向） |
| 单面读（v0.6） | `IBusinessAuditQueryService` / `BusinessAuditQueryInput`（统一 `IAuditQueryService` 为 v0.7 additive） |
| 既有查询/分析 | `IAuditLogQueryService`/`IAuditLogAnalyticsService`（随迁本包，namespace 不变） |

**不适用边界**：不搬迁框架 Core 的 `IAuditLogStore`/`AuditLogEntry`/`AuditLogFilterAttribute`（它们留框架）；不放实现类/实体/DataService（留主包）；不做跨扩展实现引用（只放契约）。

---

## 决策

1. **新建 `TKWF.Ext.AuditLogging.Abstractions`（v0.1.0）**，MinVerTagPrefix `AuditLogging.Abstractions/v`，独立版本独立发布。
2. **命名空间保持 `TKWF.Ext.AuditLogging`**（不追加 `.Abstractions`）——对齐 SecurityLog.Abstractions 零 using 迁移先例。
3. **只放契约**：接口 / record / 常量 / Options；**零实体、零 SG1、零 Initializer、零实现**。
4. **依赖最小化**：纯 BCL 为主；`AuditLoggingOptions`/`IBusinessAuditQueryService` 需 `TKWF.Domain`（`[Options]`/`IDomainService`）时按需引用，其余不引。
5. **单向依赖**：`SecurityLog.Abstractions → AuditLogging.Abstractions`（复用 `IAuditFailureHook`/事件基元）；两实现包互不引用。
6. **主包改实现**：`AuditLogging` 引本契约包并实现接口；消费方只引契约包（L2 合规）。
7. **契约版本策略**：只增不改优先；破坏性变更按 0.x minor + 适配通知。
8. **既有两契约随迁（E9，调整：v0.6 延后，v0.7 随迁）**：v0.6 保持 `IAuditLogQueryService`/`IAuditLogAnalyticsService`/`AuditLoggingOptions` 于主包（本迭代聚焦业务审计面；随迁会牵动既有测试与消费方编译面，非当前关键路径）；v0.7 随迁本包（namespace 不变、零代码改动）。期间"消费方只引契约包"目标对**既有查询面**未完全达成——已在方案 §9.2 登记，新增业务审计面已完全走契约包。

## 备选方案

| 方案 | 放弃原因 |
|---|---|
| A. 契约留在主包 | 违 ADR48 D7 / TKWF0022；消费方直引实现 |
| B. 上提框架 Core | 本仓不可改框架；业务审计非框架机制（P13） |
| C. 并入 `SecurityLog.Abstractions` | 语义错位（审计≠安全事件）；反向依赖（AuditLogging→SecurityLog） |
| D. 不建包、消费方自建 | 口径漂移（P2）；系统帧写法分裂 |
| E. 另立中立第三契约包（如 `TKWF.Ext.Audit.Abstractions`）承载失败信号 | 为 2 个类型多一条版本线（P9 成本无收益）；三面架构锚定 AuditLogging（统一读模型属主）；仓内契约→契约先例（Federation.Abstractions→TrustCenter.Abstractions）已证明直连可行 |

## 后果

- **正面**：消费方只引契约；跨扩展复用失败信号单源；实现演进不牵动消费方编译面。
- **负面/缓解**：多一个包/版本线（P9 已由本 ADR 说明必要性）；双包发布顺序需协调（Abstractions 先发）；`AuditLoggingOptions` 与既有两查询契约从主包迁至契约包属**命名空间不变的物理迁移**，消费方零代码改动（对齐 SecurityLog 先例）。

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-11-27 | 初稿骨架——新建 AuditLogging.Abstractions、命名空间保持、只放契约、单向依赖 |
| 2026-11-27 | v0.2——Oracle5 PASS WITH CONDITIONS；E9 闭环：既有两契约随迁 + 补中立第三契约包备选排除行；v0.6 单面查询（`IAuditQueryService` v0.7 additive） |
