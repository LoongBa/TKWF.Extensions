# ADR-AuditLogging-审计信号同步优先与事件契约

> **版本**：v0.2
> **状态**：**已通过 Oracle5 评审（PASS WITH CONDITIONS，E1–E10 已闭环）**；待实施
> **关联**：`docs/AuditLogging/审计三面分层与业务审计-开发方案.md` §三/§六；需求来源 DMP-Lite《审计能力装配分析》§4.3（E1–E6）+ §7 第 7 问；《设计原则手册》P2/P3/P5/P6/P7/P13/P14；`设计原则/横切设计域.md`（事件/消息，ADR21/22/24/33）
> **日期**：2026-11-27

---

## 目的与目标

确立审计生命周期信号的**可靠性分层**：审计级信号（落库失败告警、需 SIEM/合规转发的）以**同步回调为主通道**（进程内、不丢），本地事件**仅作便利扇出且默认关闭**；并给出"开启事件但宿主未启用 `AddEventDispatch()`"这一**运行时静默丢失**的根治办法（复用框架门控 + 启动醒目提示）。读者 3 句话内明白：不是不能有事件，而是**审计信号不能只靠异步事件**——写失败与派发未开叠加会双丢；正确做法是同步 hook 保底、事件默认关、复用框架门控前置校验。

## 问题

### 问题现象

1. 两扩展落库失败**仅记 Warning 后静默**（`_Framework/SecurityLog/SecurityLogStore.cs:46`；`AuditLogStore.cs:45-48`）——合规审计可能静默缺失（G7）。
2. DMP 曾遇"本地事件进 Bag 无人排空、静默丢弃"（`ChannelConfigChanged` 同源，DMP Merchant 侧尚未启用 `AddEventDispatch()`）——若扩展发布事件而宿主未开派发，事件**无声消失**。
3. 若按 DMP 初案把 E1–E6 全做成 async 事件：写失败 + 派发未开 = **双丢**；且每条审计都发事件 = 量级大 + PII + 顺序/重复问题。

### 触发场景

- 消费方需要实时看板 / SIEM 转发 / 暴力破解联动。
- 消费方需要落库失败告警（避免合规审计静默缺失）。
- 多宿主：平台宿主已启用派发、商户宿主未启用。

### 现有方案的不足

- **纯异步事件方案**：无法保证"审计不丢"（`横切设计域.md:32` 明示审计类需求应同步回调）。
- **加 `EnableEvent` 开关**：默认开启 → 未启用派发的宿主静默丢；默认关闭 → 默认无信号；若只靠开关，运行时无法发现前置缺失（P6 应上移）。
- **扩展自造 fail-fast 检测**：与框架既有 `EventDispatchFilter` 门控重复（P2 双份推导），易冲突。

## 使用场景

| 场景 | 使用方式 |
|---|---|
| 落库失败告警 | 注册 `IAuditFailureHook`（同步，恒触发），接告警/SIEM |
| 实时看板（非审计 of-record） | 开启 `PublishEvents` + 宿主 `AddEventDispatch()`；订阅 `AuditLogPersisted`/`SecurityLogPersisted` |
| 清理观测 | `AuditCleanupCompleted`（同步返回计数为权威 + 事件可选） |
| 宿主未启用派发 | **启动醒目提示 + 复用框架 `EventDispatchFilter` 门控**（E4；非扩展自造 fail-fast）；文档列为硬前置 |
| 跨进程/SIEM | 跨库用 `IOutboxExportHook`（ADR24）或水位增量拉取；**不用**本地事件跨进程 |

**不适用边界**：不把每条审计做成分布式事件；不用事件替代查询/导出/统计；不用本地事件做跨商户聚合（本地事件不出进程）。

---

## 决策

1. **信号两层**：**同步回调（主，保底）** + 本地事件（次，便利）；二者契约均在 Abstractions。
2. **失败信号恒同步**：`IAuditFailureHook.OnPersistFailed(AuditPersistFailure)` 在 store 的 catch 内同步调用，**永不抛**；`IAuditDiagnostics` 计数。
3. **事件默认关**：`AuditEventPublishingOptions.PublishEvents=false`；未见订阅者零开销。
4. **启动守卫（复用框架门控，不重复造轮子，E4）**：`PublishEvents=true` → 一次性醒目启动提示 + 依赖框架既有 `EventDispatchFilter` 门控（`[DomainEventHandler]` 声明但未调用 `AddEventDispatch()` 已被框架标记为"事件静默丢弃"警告，V4.9.102/ADR35 #6，含 runtime gate `EventDispatchFilter_NoDispatch_ReturnsFalse`）；**不**做扩展自造的激进 fail-fast。若宿主确实未启用派发，文档列为硬前置。
5. **提交后发布**：落库成功后发布；失败事件永不抛；发布失败不影响写与业务（失败隔离）。事务时序安全实证：`ILocalEventBus` Pre-commit 收集 → PostProceed 派发、永不重抛（框架 CHANGELOG v4.9.99:769）。
6. **载荷最小化**：只带 Id/时间/面/服务/方法/结果/CorrelationId；**禁带 `ArgumentsJson`/`Detail` 原文**。
7. **发布点在扩展 store**：落库成功后同栈发布（先例 `ChannelConfigChanged.cs:8-9`、`FeatureManager.cs:349-351`），非框架 filter、非消费方 Store。
8. **时序归属（§7.7）**：扩展保证"提交后发布"；**消费方保证 `AddEventDispatch()`**；扩展以醒目提示 + 框架门控暴露前置缺失。
9. **跨进程**：`IOutboxExportHook`（ADR24）或水位增量；扩展不直发 MQ。
10. **E6 导出留痕优先写业务审计**（权威），事件仅通知；**v0.6 无导出 → `AuditExportCompleted` 事件延后 v0.7**（E8，避免"声称未实现"）。

## 备选方案

| 方案 | 放弃原因 |
|---|---|
| A. E1–E6 全做 async 事件（DMP 初案） | 审计不能丢；双丢风险；量级/PII/顺序 |
| B. 只做同步回调、不出事件 | 实时看板/风控不便；但可保留为默认关的可选事件 |
| C. 事件默认开 + 文档提醒 | 未启用派发宿主静默丢（现状坑复现） |
| D. 扩展自行检测并在运行时每次检查派发 | 运行时热路径开销；应上移启动期（P6）；且与框架门控重复 |
| E. 扩展自造启动 fail-fast | 与框架 `EventDispatchFilter` 门控重复（P2）；框架未暴露稳定检测 API 时不可承诺 |

## 后果

- **正面**：失败信号不丢；事件消费方按需开启且前置缺失可见；跨进程走规范通道。
- **负面/缓解**：需要消费方注册 hook（提供默认 no-op 实现 + 文档）；事件默认关需消费方显式开启（有意为之）；框架门控为 Warning 级，需配合文档硬前置。

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-11-27 | 初稿骨架——同步优先 + 事件默认关 + 启动守卫 + 时序归属 |
| 2026-11-27 | v0.2——Oracle5 PASS WITH CONDITIONS；E4 闭环：统一为复用框架 EventDispatchFilter 门控（删扩展自造 fail-fast/两义）；E8：`AuditExportCompleted` 延后 v0.7 |
