# TKWF.Ext.AuditLogging.Abstractions 契约抽象

**状态**: 契约抽象（ADR48 D7 依赖倒置） | **框架**: .NET 10 | **依赖**: TKWF.Domain（`IBusinessAuditQueryService : IDomainService`）

## 定位

审计能力的**公开契约包**（消费方唯一编译期依赖）——把业务事实审计门面、脱敏/失败信号/访问策略、事件载荷、业务审计查询收敛于此；实现（实体/DataService/门面实现/接线）留 `TKWF.Ext.AuditLogging` 主包。属 **ADR-AuditLogging-Abstractions契约包** 落点。

> 命名空间保持 `TKWF.Ext.AuditLogging`（不追加 `.Abstractions`）——对齐 `SecurityLog.Abstractions` 零 using 迁移先例。

## 内容（v0.1.0）

| 类型 | 说明 |
|------|------|
| `IBusinessAuditWriter` | 业务事实审计写入门面（**接线型**，非 `IDomainService`；域帧 `User.GetService<T>()`，系统帧 `provider.GetRequiredService<T>()`；`Use<T>()` 约束 `IDomainService` 不适用） |
| `BusinessAuditEntry` | 业务审计条目（Actor 必填 / Target / Action / Before / After / CorrelationId / ExecutionTime） |
| `AuditActor` / `AuditActorType` | 操作者一等公民（User/System/Job/Service/Anonymous；`AuditActor.System/Job/User/Service/Anonymous` 工厂） |
| `AuditTarget` | 业务对象（TargetType + TargetId） |
| `AuditActionType` / `AuditFacet` | 常用动作常量 + 自由字符串约定；审计面枚举 |
| `IAuditActorProvider` | 可选 Actor 自动派生（默认 `null` → 强制显式） |
| `IAuditMaskingPolicy` | 脱敏策略（store 层统一应用；默认实现留主包，可 `TryAdd` 替换） |
| `IAuditFailureHook` / `AuditPersistFailure` | **同步**落库失败回调（恒触发、永不抛；禁带原文） |
| `IAuditDiagnostics` / `AuditDiagnosticsSnapshot` | 诊断计数（只读快照） |
| `IAuditAccessPolicy` / `AuditAccessRequest` / `AuditAccessDecision` / `AuditOperation` | 访问策略（**fail-closed 默认**） |
| `AuditPermissionNames` | 审计权限键常量（`AuditLogging.Query/QueryAll/Detail/Export/Delete/Purge`） |
| `AuditCorrelationId` | CorrelationId 单源（MaxLength=128，三面同域） |
| `AuditLogPersisted` / `AuditLogPersistFailed` / `SecurityLogPersisted` / `SecurityLogPersistFailed` / `AuditCleanupCompleted` | 本地事件载荷（最小化、无原文） |
| `IBusinessAuditQueryService` / `BusinessAuditQueryInput` / `BusinessAuditListItemDto` / `BusinessAuditPagedResult` | 业务审计查询（v0.6 单面；统一 `IAuditQueryService` 为 v0.7 additive） |
| `AuditFailurePolicyOptions` / `AuditEventPublishingOptions` / `AuditAccessPolicyOptions` | 配置 POCO |

## 零破坏铁律

- 命名空间 `TKWF.Ext.AuditLogging` 不变（实现类同命名空间，跨项目自动解析）。
- 仅契约（接口/record/常量/Options）；**零实体、零 SG1、零 Initializer、零实现**。
- 依赖方向单向：`SecurityLog.Abstractions → AuditLogging.Abstractions`；实现包互不引用。

## 引用关系

```
AuditLogging.Abstractions（契约）  ← SecurityLog.Abstractions（复用 IAuditFailureHook/事件基元）
        ▲                                   ▲
AuditLogging（实现）              SecurityLog（实现）
        ▲                                   ▲
        └──────── 消费方（只引 Abstractions）────────┘
```

完整设计与评审见 `docs/AuditLogging/审计三面分层与业务审计-开发方案.md` 与 `docs/AuditLogging/ADR/`。
