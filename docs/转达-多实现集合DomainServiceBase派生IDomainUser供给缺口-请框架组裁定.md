# 转达 · 多实现集合中 DomainServiceBase 派生实现的 IDomainUser 供给缺口——请框架组裁定

> **转达方**：TKWF 扩展模块组（`_TKWF.Extensions`）
> **接收方**：TKW.Framework 框架组
> **日期**：2026-10-04
> **关联**：v4.10.53 ADR90 领域自治根治（正确路线）· D17 扩展机制基座 · `DomainServiceBase.cs` · `AddConstructibleService`（`DomainServiceCollectionExtensions`）· `IDomainUser` D01（永不注册 DI）铁律
> **性质**：请裁定——多实现集合（`TryAddEnumerable`）中 DomainServiceBase 派生实现的 IDomainUser 如何供给

---

## 一、要裁定的问题（一句话）

正确路线（ADR90）确立"**IDomainUser 永不注册 DI**，领域服务继承 `DomainServiceBase` 经基类 `User` 取上下文"——但 `TryAddEnumerable` **多实现集合**中的 DomainServiceBase 派生实现（FeatureManagement 4 Provider / Notifications InboxNotifier / BackgroundJobs JobExecutionRecorder / MFA Totp+SmsMethod）由消费方**普通 DI `GetServices<T>()` 枚举构造**，集合内实现 ctor 注入 `IDomainUser` 无法被供给 → **生产解析失败**。请框架组裁定供给机制。

## 二、问题现象（失败场景）

消费方（业务域）启用含多实现集合的扩展后：

```csharp
// FeatureManagement：FeatureManager（DomainServiceBase 门面）ctor 注入 IEnumerable<IFeatureValueProvider>
var providers = sp.GetRequiredService<IEnumerable<IFeatureValueProvider>>();
// ↑ 枚举即构造集合内每个实现 → UserFeatureValueProvider 等 ctor 注入 IDomainUser → 永不注册 → 抛异常
```

- `AddConstructibleService`（单实现门面）场景已由守卫工厂解决：`User.Use<接口>()` AOP 路径设 `CurrentAopUser` → 守卫工厂经 `ActivatorUtilities.CreateInstance` **以 ambient user 注入**实现 ctor 的 `IDomainUser` 参数。
- 但 `TryAddEnumerable` 多实现集合场景**无此机制**：普通 DI `GetServices<T>()`/构造注入 `IEnumerable<T>` 枚举时，容器按实现类 ctor 解析——`IDomainUser` 不在容器（永不注册）→ 解析失败。

## 三、受影响的扩展实现（实证清单，2026-10-04 整改核实）

| 扩展 | 集合契约 | 实现（ctor 均注入 `IDomainUser`，未继承 DomainServiceBase） | 注册 | 现状 |
|------|---------|------|------|------|
| FeatureManagement | `IFeatureValueProvider`（扩展接口 : IDomainService） | User/Role/Tenant/Global 四 Provider（internal sealed，ctor `(IDomainUser user)`） | `TryAddEnumerable`（FeatureManagement V0.4.0 README 边界保留注记） | 边界保留（未改代码）——`FeatureManager` ctor 构造注入 `IEnumerable<IFeatureValueProvider>` 生产解析失败 |
| Notifications | `INotificationNotifier`（扩展接口 : IDomainService） | InboxNotifier（ctor `(IDomainUser user)`）/ EmailNotifier（ctor `(IServiceProvider, ILogger)`——接线型已改，可解析） | `TryAddEnumerable` | InboxNotifier 边界保留 |
| BackgroundJobs | `IBackgroundJobExecutionListener`（主框架 Core 契约，非 IDomainService） | JobExecutionRecorder（ctor `(IDomainUser user, ILogger)`，继承**不适用**——主框架契约） | `TryAddEnumerable` | 边界保留——依赖框架三桥 SystemActor 通道供给（ADR88 方案 i，本批不改） |
| MFA | `IMfaMethod`（扩展接口 : IDomainService） | TotpMfaMethod / SmsMfaMethod（ctor `(IDomainUser user, ...)`） | `TryAddEnumerable` | 边界保留——MFA V0.1.0 已发布，门面消费方枚举 |
| Approval | `IApprovalAssigneeResolver`（扩展接口 : IDomainService） | DefaultApprovalAssigneeResolver（**纯逻辑无 ctor 注入**——可正常解析） | `TryAddEnumerable` | 无故障（纯逻辑），仅记边界语义 |

## 四、根因分析

1. **D01 铁律**（IDomainUser 永不注册 DI）与 **多实现集合** 存在结构性冲突：单实现门面有 AOP 守卫工厂供给 ambient user，集合无等价机制。
2. `TryAddEnumerable` 按实现类型去重注册普通描述符——容器枚举时走实现类 ctor 解析路径，`IDomainUser` 参数无供给源。
3. 现有**逃生模式**（不可作为示范）：BackgroundJobs JobExecutionRecorder 依赖框架三桥（内置/Hangfire/Quartz）经 `GetServices` 枚举 + **SystemActor 通道**（`DomainUserContext.CurrentAopUser` 由三桥在执行上下文设置）供给——是框架内部既有机制的偶然复用，非通用契约。

## 五、建议方案（给框架组）

1. **A（首选，通用供给）**：为多实现集合提供"集合内 DomainServiceBase 派生实现的 ambient user 供给"机制——如 `GetServices<T>()` 枚举经 `DomainUserContext.CurrentAopUser` 非空时以 ActivatorUtilities 注入 ambient user 构造（对齐守卫工厂语义）；或提供 `TryAddEnumerableConstructible`（集合版 AddConstructibleService：接口守卫枚举 + 实现类经 ambient user 可构造）。使集合内实现可安全继承 `DomainServiceBase` 统一取上下文。
2. **B（落点框架基座）**：D17 三桥（内置/Hangfire/Quartz）执行上下文统一设置 `CurrentAopUser`（SystemActor）——集合经 `GetServices` 枚举 + 容器内 `IDomainUser` **仅以 Scoped 单例**注册为"ambient user 转发器"（从 CurrentAopUser 取值），实现类 ctor 注入 `IDomainUser` 仍可解析。但注意与 D01 冲突（D01 禁止注册 IDomainUser）——需框架组裁定豁免形态（如内部专用接口 `IAmbientUserProvider`）。
3. **C（兜底）**：扩展侧保持边界保留（不继承 DomainServiceBase、ctor 保留 IDomainUser 注入），集合契约继续依赖既有逃生通道；文档标注"集合内实现暂不支持继承 DomainServiceBase"。此方案不解决生产解析失败（FeatureManagement 现状即为失败态）。

## 六、请求

请框架组确认 A/B/C，并告知预计版本；扩展侧按裁定无损跟进（当前 4 扩展边界保留组待框架机制落地后统一迁移）。

---

<!-- EOF -->
