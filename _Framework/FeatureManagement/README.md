# TKWF.Ext.FeatureManagement

> TKWF 扩展：**功能管理（特性开关）**——编译期 Contributor 定义声明 + **Provider 链分层值（v0.2.0）** + `IFeatureChecker` 实现（接入框架 `[RequireFeature]` 过滤器）+ **变更事件（v0.2.0，v0.3.0 升级分布式）** + **复杂 ValueType 类型化读写（v0.3.0）** + 管理 API。
> 复用框架 Feature 基座（V4.9.50 ADR19 P4），数据访问全部委托 SG1 DataService（红线合规）。

## 定位

| 项 | 说明 |
|----|------|
| 包名 | `TKWF.Ext.FeatureManagement` |
| 版本 | **V0.4.4（V4.10.55 多实现集合守卫工厂——4 Provider TryAddEnumerableConstructible + 继承 DomainServiceBase，ADR92/T3 闭环）**：修复 FeatureManager 集合枚举生产失败（T3 转达 V0.4.0 边界保留组收编） |
| 依赖 | `TKWF.Domain`（含框架 `IFeatureChecker`/`RequireFeature`/`AddFeatureCheck`/`ILocalEventBus`/`IDistributedEventBus` 基座）+ SG1（接口判定收集，v4.10.31 A+ 阶段 3） |
| 数据 | 表 `FeatureValue`（框架 `SyncTables` 统一建表；`Value` 列 v0.3.0 起无界） |

## 架构分层

```
FeatureDefinition / FeatureDefinitionContext / IFeatureDefinitionContributor   # 定义层（接口判定编译期收集，v4.10.31 A+ 阶段 3）
IFeatureDefinitionRepository / InMemoryFeatureDefinitionRepository             # 定义仓库
FeatureValueEntity（SG1 声明式） → FeatureValueEntityDataService（SG1 骨架）    # 存储层（仅内部，不直通控制器）
IFeatureValueStore / FeatureValueStore（读静默/写传播）                        # Store（v0.3.0 接口 public；V0.4.0 继承 DomainServiceBase + AddConstructibleService）
IFeatureValueProvider（v0.2.0 扩展点——内置四层 + 消费方自定义插入 ProviderOrder 链）  # Provider 层
FeatureCacheVersionRegistry（v0.2.0 版本号缓存表——写后全层即时失效；v0.3.0 public）  # 缓存版本
IFeatureManager / FeatureManager（Provider 链解析 + 版本号缓存 + 管理写路径 + 类型化读写（v0.3.0）+ 变更事件发布） # 门面（V0.4.0 继承 DomainServiceBase + AddConstructibleService）
DistributedFeatureChangedHandler（v0.3.0 跨实例失效——[DomainEventHandler] + IDistributedEventHandler，SG4 自动注册） # 分布式 handler
FeatureChecker<TUserInfo>（实现框架 IFeatureChecker + ambient 用户解析）       # 检查器（V0.4.0 接线型：ctor IServiceProvider + TryAddScoped 普通 DI）
FeatureManagementApiService（[GenerateController] 管理 API——写路径委托 Manager） # 管理接口
```

- **框架基座复用（D1）**：实现框架 `TKW.Framework.Core.Features.IFeatureChecker`（fail-closed：未定义 → false）；`ConfigureFilters` 调 `builder.AddFeatureCheck()`——消费方 `[RequireFeature]` 零改动接入，不自建过滤器/异常。
- **Provider 扩展点（v0.2.0 D1）**：`IFeatureValueProvider`（Name + GetOrNullAsync）+ `FeatureOptions.ProviderOrder` 有序解析——内置四层（User/Role/Tenant/Global）+ 消费方 `AddScoped<IFeatureValueProvider, MyProvider>()` 追加（TryAddEnumerable 多实现）；Name 冲突首次解析懒校验。
- **版本号缓存（v0.2.0 D3）**：缓存 key 带版本（`Feature:{name}:v{version}:...`）——写后 `version++` 全层（User/Role/Tenant/Global）即时失效（修复 v0.1.0 动态 key 仅 TTL 收敛缺陷）；旧 key TTL 清理无泄漏。
- **变更事件（v0.2.0 D2 → v0.3.0 分布式）**：写路径（Set/Delete）Commit 后 `ILocalEventBus.PublishAsync(new FeatureValueChangedEvent(...))`——事件标 `[DistributedEvent]`（v0.3.0），AOP 路径经框架 `EventDispatchFilter` 自动路由：注册真实分布式总线（RabbitMQ）→ 分布式广播（多实例即时失效 + 外部服务订阅）；默认（LocalDistributedEventBus）→ 进程内。**跨实例失效内建**：`DistributedFeatureChangedHandler`（`[DomainEventHandler]` + `IDistributedEventHandler`，SG4 消费方编译期自动注册）收到远程事件 bump 版本表——消费方无需自接总线。
- **类型化读写（v0.3.0）**：`GetValueAsync<T>`/`SetValueAsync<T>`——序列化/反序列化映射集中于 `FeatureManager`（bool/数字/DateTime 规范字符串 + 其他类型 JSON）+ 写时校验（对齐定义 `ValueType`，违反 → `ArgumentException`；未定义 Feature 跳过校验向后兼容）。
- **SG1 收集（C1）**：主框架 V4.9.114 起编译期收集——业务模块实现 `IFeatureDefinitionContributor` + `Define` 声明定义（v4.10.31 A+ 阶段 3 起纯接口判定，不再用 `[FeatureContributor]` 特性）。
- **用户契约（C2）**：`IFeatureManager` 接收 `IDomainUser`；`FeatureChecker` 解析 ambient 用户（`DomainUserContext.CurrentAopUser as IDomainUser`）。
- **注册形态（V0.4.0 领域自治根治，ADR90——正确路线三态）**：
  - **门面（AddConstructibleService）**——`IFeatureValueStore` / `IFeatureManager`（接口 `: IDomainService`）：接口可构造守卫工厂（`CurrentAopUser` 守卫）+ 实现类 throw-factory；实现继承 `DomainServiceBase`（经基类 `User` 取上下文——**IDomainUser 永不注册 DI**，旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败，v0.3.3 同根缺陷）+ `[DiContractIgnore]` 豁免 DI001；消费方统一经 `User.Use<IFeatureValueStore>()` / `User.Use<IFeatureManager>()` 解析。
  - **接线型（TryAddScoped 普通 DI）**——`IFeatureChecker`（主框架 Core 契约非 IDomainService 不可修改）：框架 `FeatureFilterAttribute` 经 `context.ServiceProvider.GetService<IFeatureChecker>()` 普通 DI 解析；实现 ctor(`IServiceProvider`, ILogger) + C1 延迟解析 `IFeatureManager`（`GetRequiredService`）——修复真实生产故障（旧 ctor(IDomainUser) 在过滤器 GetService 解析时构造失败 → 特性检查静默失效）。
  - **多 Provider（TryAddEnumerableConstructible，V4.10.55 ADR92/T3 闭环）**——`IFeatureValueProvider` 内置四层：集合版守卫工厂（实现类 throw-factory + 集合元素守卫工厂，帧内 CurrentAopUser 供给 ctor IDomainUser；帧外枚举抛守卫）；实现继承 DomainServiceBase + `[DiContractIgnore]`。`FeatureManager` 经 `User.Use<IFeatureManager>()` 帧内创建时 ctor 注入 `IEnumerable<IFeatureValueProvider>` 在帧内枚举成功——修复原 TryAddEnumerable 按实现类 ctor 激活、IDomainUser 永不注册（D01）→ 生产解析失败（T3 转达 §三 实证，V0.4.0 边界保留组收编）。
- **管理 API（C3）**：`FeatureManagementApiService`（`[GenerateController]`）写路径委托 `IFeatureManager`——缓存失效 + Global 唯一性 + 写时校验在门面处理；DataService 仅内部存储（裸 CRUD 禁直通）。

## 核心能力

### 定义声明（业务模块）

```csharp
// v4.10.31 A+ 阶段 3 起纯接口判定（无需 [FeatureContributor] 特性）
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
- **多实例缓存边界（C5 → v0.3.0 内建失效）**：`IMemoryCache` 进程内；v0.3.0 起 `FeatureValueChangedEvent` 标 `[DistributedEvent]` + 内建 `DistributedFeatureChangedHandler`——注册真实分布式总线（RabbitMQ）时多实例改值全实例即时失效；未接总线（默认 LocalDistributedEventBus）跨实例仍靠 TTL 收敛。
- **写时校验（v0.3.0）**：`SetValueAsync` 按定义 `ValueType` 校验（违反 → `ArgumentException`——Boolean/Int/Decimal/DateTime/Json/String）；**未定义 Feature 跳过校验**（向后兼容）。行为变更：向已定义 Boolean Feature 写 "abc" v0.2.0 静默成功 → v0.3.0 抛异常。
- **Provider 冲突（v0.2.0）**：注册 Provider Name 重复 → 首次解析抛 `InvalidOperationException`（懒校验）。
- **红线合规**：Store/Manager/内置 Provider 无 `IFreeSql`/`IEntityDAC`（全经 DataService 委托）；物理缓存经 `IMemoryCache` 注入。
- **管理写路径**：仅 `IFeatureManager.SetValueAsync/DeleteValueAsync` 为写入口（失效 + 唯一性 + 写时校验 + 事件发布）；DataService 直写绕过（设计上禁止直通控制器）。

## 启用方式（v4.9.85+）

```csharp
using TKWF.Ext.FeatureManagement;

[TKWFEnabledExtension(typeof(FeatureManagementExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo> { ... }
```

三钩子自动接线（含 `AddFeatureCheck` 过滤器）。自动注册（V0.4.0 领域自治根治，ADR90——按"正确路线"注册形态）：

| 接口 | 实现 | 注册形态 | 消费方式 |
|------|------|---------|---------|
| `IFeatureValueStore` | `FeatureValueStore`（继承 `DomainServiceBase`） | **`AddConstructibleService`**（接口可构造守卫工厂 + 实现类 throw-factory） | `User.Use<IFeatureValueStore>()` |
| `IFeatureManager` | `FeatureManager`（继承 `DomainServiceBase`） | **`AddConstructibleService`** | `User.Use<IFeatureManager>()` |
| `IFeatureChecker` | `FeatureChecker<TUserInfo>` | **接线型普通 DI**（TryAddScoped，ctor IServiceProvider + C1 延迟解析 Manager——无 IDomainUser） | 框架 `FeatureFilterAttribute` 经 `GetService` 解析 |
| `IFeatureValueProvider` ×4 | User/Role/Tenant/Global | **TryAddEnumerable**（多实现集合，边界保留） | `FeatureManager` 构造注入集合 |

跨实例失效 handler（`DistributedFeatureChangedHandler`）由消费方 SG4 编译期自动注册（`[DomainEventHandler]`）——零手动接线。

## 数据模型

```sql
FeatureValue(id BIGINT PK, name VARCHAR(128), value TEXT NULL, provider_name VARCHAR(32),
             provider_key VARCHAR(128) NULL, description VARCHAR(512) NULL, is_visible_to_clients BOOL,
             create_time TIMESTAMP, update_time TIMESTAMP)
-- 唯一约束：UX_FeatureValue_Name_Provider（name,provider_name,provider_key）——Global 层唯一性由应用层保证（C4）
-- v0.3.0：value 列 VARCHAR(512) → 无界（SQLite=text/Pg=text/SqlServer=nvarchar(max)）——容纳 JSON 复杂值
```

- 物理删除（不声明 `IsDeleted`，`hasSoftDelete:false`）。
- 生产建表：框架 `SyncTables` 统一托管（ADR49），**无手工 DDL 前置**（存量库加宽依赖 SyncTables 自动 ALTER，若未自动需 DBA 手动变更——见使用指南 §6）。

## 版本演进

### V0.4.0（V4.10.53 领域自治根治，ADR90——正确路线，2026-10-04）

- **`FeatureValueStore` / `FeatureManager` 继承 `DomainServiceBase`**（经基类 `User` 获取用户上下文——**IDomainUser 永不注册 DI**，旧 TryAddScoped 构造注入 IDomainUser / 工厂 lambda `sp.GetRequiredService<IDomainUser>()` 生产解析必失败——v0.3.3 同根缺陷）+ `[DiContractIgnore]` 豁免 DI001；DataService/Store 仍经 `User.Use<T>()` 懒加载（DI004 零豁免）
- **注册形态改 `AddConstructibleService`**（IFeatureValueStore / IFeatureManager）——接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；消费方统一 `User.Use<IFeatureValueStore>()` / `User.Use<IFeatureManager>()` 解析（AOP 路径设 CurrentAopUser → GetRequiredService）
- **`FeatureChecker<TUserInfo>` 改接线型**（skill §4.2/§4.8 #7）——`IFeatureChecker` 为主框架 Core 契约（非 IDomainService 不可修改），框架 `FeatureFilterAttribute` 经 `context.ServiceProvider.GetService<IFeatureChecker>()` 普通 DI 解析；ctor 改 `IServiceProvider` + C1 延迟解析 `IFeatureManager`（GetRequiredService）；注册保持 TryAddScoped。**修复真实生产故障**：旧 ctor(IDomainUser)（永不注册 DI）在框架过滤器 GetService 解析时构造失败 → 特性检查静默失效
- **4 Provider 边界保留**（User/Role/Tenant/Global，TryAddEnumerable 多实现集合）——ctor 需 IDomainUser 经普通 DI GetServices 构造时 IDomainUser 永不注册 → 生产解析失败（**框架机制缺口 T3 转达候选**，本批不改，对齐 Authentication Provider 处理）
- 测试宿主走生产路径：真实 DI（Initializer ConfigureServices + FreeSql SQLite + AddLogging）+ `DomainUser<TestUserInfo>.BindScope` + `User.Use<接口>()` AOP 路径 + 接线型 GetRequiredService 可解析哨兵 + Initializer 注册形态断言（守卫工厂/throw-factory/域外抛/4 Provider TryAddEnumerable）；**134 用例全绿**（127→134：+5 Initializer 形态断言 +2 Checker 接线型哨兵；禁止 slnx 构建，仅本扩展项目 + 测试项目）

### V0.4.4（V4.10.55 多实现集合守卫工厂，ADR92/T3 闭环，2026-10-04）

- **4 Provider（User/Role/Tenant/Global）改 `TryAddEnumerableConstructible`（集合版守卫工厂）**（V4.10.55 ADR92/T3 方案 A 落地）——Provider 继承 `DomainServiceBase`（经基类 `User` 取上下文，删 `_user` 字段）+ `[DiContractIgnore]` 豁免 DI001；注册由 `TryAddEnumerable` 改 `TryAddEnumerableConstructible<IFeatureValueProvider, {Provider}>()` ×4（集合版 `AddConstructibleService`：实现类 throw-factory + 集合元素守卫工厂——`FeatureManager` 经 `User.Use<IFeatureManager>()` 帧内创建时 ctor 注入的 `IEnumerable<IFeatureValueProvider>` 在帧内枚举，守卫工厂经 `CurrentAopUser` 供给 ctor 的 `IDomainUser`）
- **修复生产失败态**：原 TryAddEnumerable 集合按实现类 ctor 激活、`IDomainUser` 永不注册（D01）→ `FeatureManager` 构造注入 `IEnumerable` 时生产解析失败（T3 转达 §三 实证，V0.4.0 边界保留组）；帧外枚举（控制器 `[FromServices] IEnumerable` 等）抛守卫（禁止形态）
- 测试宿主同步：`FeatureManagementTestHost` 桩 `Provider` 显式注入（守卫工厂从 CurrentAopUser 直供 user，DI 桩注册不再被触发解析）+ Initializer 断言改守卫工厂形态 + ProviderImpl throw-factory 哨兵；**135 用例全绿**

## 后续演进（v0.4.0+ 候选）

管理 UI（需推翻 v0.1.0「UI 归消费方」裁定）；定时开关/计划切换（`FeatureValueType.DateTime` 已铺路）；Json 值 schema 校验（JsonSchema 集成）；`AllowedProviders` Contributor 端声明用例补全。
