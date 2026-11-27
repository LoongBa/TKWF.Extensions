# ADR-AuditLogging-审计三面分层与业务审计

> **版本**：v0.2
> **状态**：**已通过 Oracle5 评审（PASS WITH CONDITIONS，E1–E10 已闭环）**；待实施
> **关联**：`docs/AuditLogging/审计三面分层与业务审计-开发方案.md` §三/§五/§七；需求来源 DMP-Lite《审计能力装配分析-转扩展组》§3/§4（G1/G2）；《设计原则手册》P2/P3/P5/P8/P15；`设计原则/扩展模块.md`；`设计原则/横切设计域.md`（多租户）
> **日期**：2026-11-27

---

## 目的与目标

在 TKWF 审计能力中补齐"业务事实审计"这一面，并把审计治理确立为**三面分层架构**：调用审计（AuditLogging 现有）、安全事件审计（SecurityLog 现有）、业务事实审计（本 ADR 新增）。目标是让任何消费方都能以**统一形状**（Actor + Target + Action + Before/After + CorrelationId）记录"某业务对象在某时刻被谁改成了什么"，同时**不修改框架 Core 的 `AuditLogEntry`/`SecurityLogEntry` record**。读者 3 句话内明白：业务审计不是"多打一条方法日志"，它需要独立实体与独立门面；三面写表分立（每面单一事实源），读侧可经统一门面按 CorrelationId 串联。

## 问题

### 问题现象

1. `AuditLogEntity` 只有方法调用 10 字段，`SecurityLogEntry` 只有 9 字段——**两者都没有 `TargetType`/`TargetId`/`ActionType`/旧值→新值**（G1 证据：`_Framework/AuditLogging/Entities/AuditLogEntity.g.cs`、`_Framework/SecurityLog.Abstractions/SecurityLogEntry.cs:19-28`）。
2. 业务动词（开户/合约暂停/白名单变更/配置变更）落入 SecurityLog 的 11 个认证事件常量之外（`SecurityLogEventTypes`）——只能塞字面量，聚合口径漂移（G2）。
3. 系统帧（定时任务）执行者无法表达：现有实体无 Actor 模型，空用户名即丢失"系统执行"语义。

### 触发场景

- 消费方需记录实体/业务对象生命周期（创建/变更/状态流转/授权/吊销）。
- 定时任务/后台作业（到期暂停、到期提醒）需以系统身份留痕。
- 合规审计需按业务对象或操作者反查"这个商户/合约被谁改过"。

### 现有方案的不足

- **方法日志充数**：把业务变更塞进 `ArgumentsJson`——形状不对（一次调用 ≠ 一次业务事实）、无法按 Target 反查、原文易泄露。
- **塞进 SecurityLog**：安全事件是认证/授权域，业务动词不在其常量集，混装破坏"一表一语义"（P2）。
- **改框架 Core record**：`AuditLogEntry` 属框架 Core，本仓不可改；且方法调用面加业务字段会让两个域耦合。

## 使用场景

| 场景 | 使用方式 |
|---|---|
| 消费方记录业务对象变更 | 域帧：`User.GetService<IBusinessAuditWriter>().SaveAsync(entry)`（`Use<T>()` 约束 `IDomainService`，接线型不适用）；系统帧：`provider.GetRequiredService<IBusinessAuditWriter>()`（接线型 TryAddScoped，先例 `AuditLogStore.cs:22-34`） |
| Actor 来源 | **必填显式**（`AuditActor.User/System/Job/...`）；或注册 `IAuditActorProvider` 自动派生（默认 null→强制显式） |
| 定义业务动词 | `AuditActionType` 常用常量，或自由字符串（约定 `{domain}.{entity}.{verb}`） |
| 按业务对象反查 | `BusinessAuditQueryInput{ TargetType, TargetId }`（v0.6 `IBusinessAuditQueryService`） |
| 串联同链路三面 | 三面同带 `CorrelationId`，经 `IAuditQueryService` 按 CorrelationId 聚合（v0.7） |
| 系统定时任务留痕 | `AuditActor.System("job:contract-expire")` |

**不适用边界**：不定义具体业务动词的业务含义（消费方自治）；不替代 `EntityHistoryFilter`（字段级变更追踪，框架机制）；不做跨库聚合（本地/单库边界）；不规定保留期限（消费方配置）。

---

## 决策

1. **三面分层**：写模型分三表（`TKWF_AuditLog` / `TKWF_SecurityLog` / 新 `TKWF_BusinessAudit`），**每面单一事实源**（P2）；不合并为一张大表。
2. **业务审计独立实体 + 独立门面**：`BusinessAuditEntity` + `IBusinessAuditWriter` 放 AuditLogging；契约下沉新建 `AuditLogging.Abstractions`（ADR48 D7）。**注册形态（E1）**：接线型 `TryAddScoped`（ctor `IServiceProvider`+`ILogger`+`IOptions`，懒解析 DataService），域帧 `User.GetService<T>()`、系统帧 `provider.GetRequiredService<T>()`（`Use<T>()` 约束 `: IDomainService`，接线型不适用）。
3. **Actor 一等公民（E1）**：`AuditActor(ActorType, Id, DisplayName)` 表达 User/System/Job/Service/Anonymous；**必填显式传入**（P5），系统帧不依赖 `IDomainUser`；可选 `IAuditActorProvider` 自动派生（默认 null→强制显式）。
4. **Action 开放而非闭集**：常用动词常量 + 自由字符串约定（避免每加业务动词改框架，P3 反向）。
5. **不改框架 Core**：`AuditLogEntry`/`AuditLogFilterAttribute`/`IAuditLogStore` 保持不动。
6. **读模型单源、写模型分立（E7）**：v0.6 交付 `IBusinessAuditQueryService`（单面、诚实命名）；v0.7 再加 `IAuditQueryService` 统一门面（`AuditRecordView` 含 `AuditFacet`，additive，不预留死接口），不建统一写表。
7. **TenantId 预留**：`BusinessAuditEntity.TenantId`（可空单义，DB-per-tenant 恒定），P15 键空间前瞻；**不实现 `IEntityTenant`**（E6，对齐 WeChatChannels C-REV-1 DB-per-tenant 永久边界先例）。
8. **暴露面（E6）**：BusinessAudit DataService 标准 CRUD **排除 `BeforeJson`/`AfterJson`**（比照 V0.4.0 `ArgumentsJson` ExcludeMethods 先例，防 PII）。

## 备选方案

| 方案 | 放弃原因 |
|---|---|
| A. 在 `AuditLogEntity` 加 Target/Action/diff 列 | 与方法调用面耦合；改框架语义边界；一表两义（P2） |
| B. 把业务事件塞进 SecurityLog（扩常量） | 安全域与业务域混装；常量膨胀；聚合口径漂移 |
| C. 消费方自建业务审计实体（不提供门面） | 每个消费方重造形状 → 口径漂移；系统帧写法各异；无法统一读 |
| D. 三面合并单表 + 类型列 | 保留/权限/形状差异被迫兼容；查询与索引退化 |

## 后果

- **正面**：业务审计有标准形状与门面；消费方只引 Abstractions；系统帧可留痕；三面可按 CorrelationId 串联。
- **负面/缓解**：新增表与 Abstractions 包（迁移成本）→ 开发环境自动建表、生产给 DDL；业务审计体量增长 → 保留策略独立配置；三面统一读为 v0.7（v0.6 先覆盖业务面）；Actor 显式传入略增调用成本（换取可判定、无隐式依赖）。

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-11-27 | 初稿骨架——三面分层 + 业务审计独立实体/门面 + Actor 模型 |
| 2026-11-27 | v0.2——Oracle5 PASS WITH CONDITIONS；闭环 E1（注册形态表/域帧消费/Actor 必填显式 + `IAuditActorProvider`）、E7（v0.6 单面查询，v0.7 additive）、E6（不实现 IEntityTenant + 暴露面排除） |
