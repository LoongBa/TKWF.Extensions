# TKWF.Ext.FeatureManagement

> TKWF 扩展：**功能管理（特性开关）**——编译期 Contributor 定义声明 + **Provider 链分层值（v0.2.0）** + `IFeatureChecker` 实现（接入框架 `[RequireFeature]` 过滤器）+ **变更事件（v0.2.0）** + 管理 API。
> 复用框架 Feature 基座（V4.9.50 ADR19 P4），数据访问全部委托 SG1 DataService（红线合规）。

## 定位

| 项 | 说明 |
|----|------|
| 包名 | `TKWF.Ext.FeatureManagement` |
| 版本 | v0.2.0（增量——Provider 扩展点 + 变更事件 + 版本号缓存失效） |
| 依赖 | `TKWF.Domain`（含框架 `IFeatureChecker`/`RequireFeature`/`AddFeatureCheck`/`ILocalEventBus` 基座）+ SG1（含 `[FeatureContributor]` 收集，V4.9.114） |
| 数据 | 表 `FeatureValue`（框架 `SyncTables` 统一建表） |

## 架构分层

```
FeatureDefinition / FeatureDefinitionContext / IFeatureDefinitionContributor   # 定义层（[FeatureContributor] 编译期收集）
IFeatureDefinitionRepository / InMemoryFeatureDefinitionRepository             # 定义仓库
FeatureValueEntity（SG1 声明式） → FeatureValueEntityDataService（SG1 骨架）    # 存储层（仅内部，不直通控制器）
IFeatureValueStore / FeatureValueStore（读静默/写传播）                        # Store
IFeatureValueProvider（v0.2.0 扩展点——内置四层 + 消费方自定义插入 ProviderOrder 链）  # Provider 层
FeatureCacheVersionRegistry（v0.2.0 版本号缓存表——写后全层即时失效）          # 缓存版本
IFeatureManager / FeatureManager（Provider 链解析 + 版本号缓存 + 管理写路径 + 变更事件发布） # 门面
FeatureChecker<TUserInfo>（实现框架 IFeatureChecker + ambient 用户解析）       # 检查器
FeatureManagementApiService（[GenerateController] 管理 API——写路径委托 Manager） # 管理接口
```

- **框架基座复用（D1）**：实现框架 `TKW.Framework.Core.Features.IFeatureChecker`（fail-closed：未定义 → false）；`ConfigureFilters` 调 `builder.AddFeatureCheck()`——消费方 `[RequireFeature]` 零改动接入，不自建过滤器/异常。
- **Provider 扩展点（v0.2.0 D1）**：`IFeatureValueProvider`（Name + GetOrNullAsync）+ `FeatureOptions.ProviderOrder` 有序解析——内置四层（User/Role/Tenant/Global）+ 消费方 `AddScoped<IFeatureValueProvider, MyProvider>()` 追加（TryAddEnumerable 多实现）；Name 冲突首次解析懒校验。
- **版本号缓存（v0.2.0 D3）**：缓存 key 带版本（`Feature:{name}:v{version}:...`）——写后 `version++` 全层（User/Role/Tenant/Global）即时失效（修复 v0.1.0 动态 key 仅 TTL 收敛缺陷）；旧 key TTL 清理无泄漏。
- **变更事件（v0.2.0 D2）**：写路径（Set/Delete）Commit 后 `ILocalEventBus.PublishAsync(new FeatureValueChangedEvent(...))`——消费方 `[DomainEventHandler]` 订阅（审计/跨实例联动）；进程内失效不依赖事件（version++ 已即时）。
- **SG1 收集（C1）**：主框架 V4.9.114 新增 `[FeatureContributor]` 编译期收集——业务模块标注 + `Define` 声明定义。
- **用户契约（C2）**：`IFeatureManager` 接收 `IDomainUser`；`FeatureChecker` 解析 ambient 用户（`DomainUserContext.CurrentAopUser as IDomainUser`）。
- **管理 API（C3）**：`FeatureManagementApiService`（`[GenerateController]`）写路径委托 `IFeatureManager`——缓存失效 + Global 唯一性在门面处理；DataService 仅内部存储（裸 CRUD 禁直通）。

## 核心能力

### 定义声明（业务模块）

```csharp
[FeatureContributor]
public class OrderFeatureContributor : IFeatureDefinitionContributor
{
    public void Define(FeatureDefinitionContext context)
    {
        context.Add(new FeatureDefinition
        {
            Name = "Order.NewCheckout",       // 必填唯一
            DisplayName = "新结算流程",
            Group = "Order",
            ValueType = FeatureValueType.Boolean,
            DefaultValue = "false"            // 默认关闭（fail-closed）
        });
        context.Add(new FeatureDefinition { Name = "App.Theme", ValueType = FeatureValueType.String, DefaultValue = "light" });
    }
}
```

### 分层值解析（Provider 链，v0.2.0）

| 层 | Provider | ProviderKey | 优先级 |
|----|----------|-------------|:------:|
| User | `IFeatureValueProvider`（内置） | UserId | 1（最优先） |
| Role | `IFeatureValueProvider`（内置） | 角色名（遍历序首个命中） | 2 |
| Tenant | `IFeatureValueProvider`（内置） | TenantId | 3 |
| **自定义**（v0.2.0） | `AddScoped<IFeatureValueProvider, MyProvider>()` | 自取（注入上下文） | ProviderOrder 配置 |
| Global | `IFeatureValueProvider`（内置） | null | 末尾 |
| 默认 | — | — | `FeatureDefinition.DefaultValue`，无则调用方参数 |

- **ProviderOrder**（`FeatureOptions.ProviderOrder`，默认 `["User","Role","Tenant","Global"]`）——未列入的自定义 Provider 追加末尾；空列表 = 注册顺序。
- 匿名（`!IsAuthenticated`）直查 Global → 默认。
- **缓存（v0.2.0）**：版本号 key（`Feature:{name}:v{version}:{provider}:{key}`）+ `NotFoundSentinel` 负缓存——写后 version++ 全层即时失效。
- **默认值语义**：显式 `defaultValue` 参数优先 → 定义 `DefaultValue` → 空。

### 变更事件（v0.2.0）

```csharp
[DomainEventHandler]
public class FeatureChangedAuditHandler : ILocalEventHandler<FeatureValueChangedEvent>
{
    public Task HandleEventAsync(FeatureValueChangedEvent e)
    {
        // e.Name / e.ProviderName / e.ProviderKey / e.OldValue / e.NewValue——审计联动/跨实例接自有总线
        return Task.CompletedTask;
    }
}
```

- 写路径（Set/Delete）Commit 后发布；进程内缓存失效不依赖事件（version++ 已即时）；跨实例由消费方订阅后接自有总线。

### 检查器（框架 `[RequireFeature]`）

```csharp
[TKWFEnabledExtension(typeof(FeatureManagementExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }

[RequireFeature("Order.NewCheckout")]      // 未启用 → FeatureDisabledException（fail-closed）
public Task CheckoutAsync() { ... }
```

`FeatureChecker<TUserInfo>` 实现框架 `IFeatureChecker`——空名/未定义/布尔解析失败均返回 false（不抛异常）。

### 管理 API（`FeatureManagementApiService`）

```csharp
// 经 IFeatureManager 写（缓存失效 + Global 唯一性）
await manager.SetValueAsync("Order.NewCheckout", "true", FeatureProviders.Global, null);   // 全站开启
await manager.SetValueAsync("Order.NewCheckout", "false", FeatureProviders.User, "u-100"); // 指定用户关闭
await manager.DeleteValueAsync("App.Theme", FeatureProviders.Global, null);
```

消费方 SG1 生成 REST 端点（`[GenerateController]`——Set/Delete 写路径委托 Manager，读路径静默降级）。

## Options 配置（`TKWF:FeatureManagement`）

| 键 | 默认 | 说明 |
|----|------|------|
| `CacheExpirationSeconds` | `300` | 值缓存 TTL（进程内 IMemoryCache；写后 version++ 即时失效，TTL 仅清理旧 key） |
| `ProviderOrder` | `["User","Role","Tenant","Global"]` | Provider 解析顺序（v0.2.0；空 = 注册顺序） |

## 约束与语义

- **fail-closed 铁律（P2）**：无 `FailClosedOnUndefined` 开关——`IFeatureChecker` 未定义恒 false（框架接口契约）；布尔值命中层存在但解析失败 → false（不跨层回退，P3）。
- **Role 层语义（P4）**：多角色按 `IDomainUser.UserInfo.Roles` 遍历顺序命中（角色间无业务优先级）；缓存 key 逐角色独立。
- **Global 层唯一性（C4）**：`ProviderKey=null` 可空唯一索引 NULL 互不相同（SQLite/PostgreSQL）——`SetValueAsync` Global 分支事务包裹 + 事务内二次校验（命中更新/未命中创建）。
- **缓存即时性（v0.2.0）**：写后 `version++`——User/Role/Tenant/Global 全层立即失效（v0.1.0 缺陷修复：动态 ProviderKey 曾依赖 TTL 最长 300s 收敛）；旧 key TTL 清理无泄漏。
- **多实例缓存边界（C5）**：`IMemoryCache` 进程内——跨实例经 `FeatureValueChangedEvent` 消费方订阅后接自有总线；本扩展零外部依赖。
- **Provider 冲突（v0.2.0）**：注册 Provider Name 重复 → 首次解析抛 `InvalidOperationException`（懒校验）。
- **红线合规**：Store/Manager/内置 Provider 无 `IFreeSql`/`IEntityDAC`（全经 DataService 委托）；物理缓存经 `IMemoryCache` 注入。
- **管理写路径**：仅 `IFeatureManager.SetValueAsync/DeleteValueAsync` 为写入口（失效 + 唯一性 + 事件发布）；DataService 直写绕过（设计上禁止直通控制器）。

## 启用方式（v4.9.85+）

```csharp
using TKWF.Ext.FeatureManagement;

[TKWFEnabledExtension(typeof(FeatureManagementExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

三钩子自动接线（含 `AddFeatureCheck` 过滤器）。DI 一律 `TryAddScoped`——消费方可自定义 `IFeatureManager`/`IFeatureValueStore` 实现优先。

## 数据模型

```sql
FeatureValue(id BIGINT PK, name VARCHAR(128), value VARCHAR(512) NULL, provider_name VARCHAR(32),
             provider_key VARCHAR(128) NULL, description VARCHAR(512) NULL, is_visible_to_clients BOOL,
             create_time TIMESTAMP, update_time TIMESTAMP)
-- 唯一约束：UX_FeatureValue_Name_Provider（name,provider_name,provider_key）——Global 层唯一性由应用层保证（C4）
```

- 物理删除（不声明 `IsDeleted`，`hasSoftDelete:false`）。
- 生产建表：框架 `SyncTables` 统一托管（ADR49），**无手工 DDL 前置**。

## 后续演进（v0.2.0+ 候选）

`IFeatureValueProvider` 自定义扩展点；复杂 ValueType（Json/DateTime）；管理 UI；Feature 变更事件（跨实例缓存失效）；定时开关。
