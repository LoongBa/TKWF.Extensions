# TKWF.Ext.Settings 设置管理扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.2.0 (分层读写 + 内存缓存 + Options 绑定修复) | **框架**: .NET 10

**核心约束**: 分层键值对存储、FreeSql 持久化、异常静默处理、SG1 声明式实体、内存缓存

---

## 一、需求分析 (Demand Analysis)

在领域驱动设计 (DDD) 的应用层中，常需对系统设置进行分层管理——不同租户/用户可能拥有不同的配置值，同时需要持久化到数据库。

- **配置分散**：appsettings.json 不支持运行时动态修改，且无法按用户/租户分层。

- **硬编码问题**：默认值散落在代码中，无法统一管理和审计。

- **持久化耦合**：设置存储直接依赖特定 ORM（FreeSql/EF Core），扩展无法做到 ORM 无关。

- **分层需求**：不同级别的设置（全局/租户/用户）需要不同的读取优先级。

---

## 二、设计原理 (Design Principles)

本扩展采用 **"领域服务门面 + SG1 DataService + 分层读取 + 异常静默"** 架构（**V0.3.0 正确路线**：扩展=业务领域开发——门面继承 `DomainServiceBase`，数据访问经 SG1 DataService，消费方 `User.Use<T>()`）。

### 1. 结构分层

- **领域服务门面 (`ISettingManager`)**：分层读写接口，屏蔽 Provider 细节，提供类型安全的 Get/Set——`internal sealed class SettingManager : DomainServiceBase, ISettingManager`（经基类 `User` 获取用户上下文，IDomainUser 永不注册 DI）。

- **数据访问 (`SettingEntityDataService`)**：SG1/xCodeGen 生成 DataService（`DomainDataServiceBase<,>` 派生 + 手写分部业务方法 `GetByKeyAsync`/`GetListByProviderAsync`/`UpsertByKeyAsync`/`DeleteByKeyAsync`）——门面经 `User.Use<SettingEntityDataService>()` NoAop 路径直建。

- **管理器实现 (`SettingManager`)**：分层查找逻辑（User → Tenant → Global → 默认值），JSON 序列化支持，内存缓存。

- **声明式实体 (`SettingEntity`)**：SG1 化实体，`partial class` + `[DomainGenerateCode]`，FreeSql `[Column]` 特性。

> **V0.3.0（领域自治根治，ADR90）**：删除 `ISettingStore`/`SettingStore` 伪 DataService 层（职责与 DataService 完全重叠）；门面继承 `DomainServiceBase` + 注册改 `AddConstructibleService`（接口可构造守卫工厂 + 实现类 throw-factory）。

### 2. 安全语义

- **异常静默**：读写失败时记录 Warning 日志，不抛出异常（不阻塞业务调用——对齐 UserCenter 降级矩阵）。

- **域作用域守卫**：`ISettingManager` 经 `AddConstructibleService` 注册——DI 中唯一可解析的是接口本身，且解析必须处于 `User.Use<T>()` 调用链内（`CurrentAopUser` 守卫）；实现类注册为 throw-factory（禁直接 DI 解析）。

- **Scoped 生命周期**：`ISettingManager` Scoped，自动参与当前请求上下文。

### 3. 与主框架的关系

- `IDomainUser` 由主框架定义（**永不注册 DI**——D01）；门面经基类 `DomainServiceBase.User` 获取用户上下文。
- 本扩展提供 `SettingManager`（继承 `DomainServiceBase`）+ `SettingsExtensionInitializer` 注册（`AddConstructibleService<ISettingManager, SettingManager>`）。
- 消费方经 `User.Use<ISettingManager>()` 进行设置读写，无需关心 Provider 细节。

---

## 三、使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.Settings` 包，扩展经 `[TKWFExtension]` 被 SG1 编译期发现（生成能力清单）。**V4.9.85 起发现不自动启用**——消费方须在自身领域初始化器上声明白名单，三钩子才接线：

```csharp
[TKWFEnabledExtension(typeof(SettingsExtensionInitializer<>))]
public class XxxDomainInitializer : DomainHostInitializerBase<XxxUserInfo> { ... }
```

白名单声明后自动注册：`ISettingManager`（默认 `SettingManager`，`AddConstructibleService` 接口守卫工厂）+ `IMemoryCache`（默认 `MemoryCache`）。

### 2. 读写设置

```csharp
// V0.3.0：领域服务门面经 User.Use<ISettingManager>() 解析（禁构造注入——DI004 零豁免）
public class MyService : DomainServiceBase
{
    public MyService(IDomainUser user) : base(user) { }

    public async Task<string> GetThemeAsync()
    {
        var settingManager = User.Use<ISettingManager>();
        return await settingManager.GetAsync("Theme", "light");
    }

    public async Task<int> GetMaxRetriesAsync()
    {
        return await settingManager.GetAsync("MaxRetries", 3);
    }

    public async Task SetThemeAsync(string theme)
    {
        await settingManager.SetAsync("Theme", theme);
    }
}
```

### 3. 配置选项

通过 `appsettings.json` 配置：

```json
{
  "TKWF": {
    "Settings": {
      "DefaultSettingValueProvider": "Global",
      "IsEnabled": true,
      "CacheExpirationSeconds": 300
    }
  }
}
```

> **Options 绑定（V0.2.0）**：`SettingsOptions` 标注 `[Options("TKWF:Settings")]`——SG1 在消费方生成
> `GeneratedOptionsBindings`，宿主启动期经 `RegisterOptionsBindings` 自动执行
> `services.Configure<SettingsOptions>(configuration.GetSection("TKWF:Settings"))`（与 Navigation/Permissions 同模式）。
> 亦可在消费方 `ConfigureExtensions` 中 `services.Configure<SettingsOptions>(o => ...)` 覆盖。

### 4. 数据访问（V0.3.0 起：SG1 DataService，无 Store 层）

设置数据访问一律经 SG1/xCodeGen 生成的 `SettingEntityDataService`（`DomainDataServiceBase<,>` 派生）——门面 `SettingManager` 经 `User.Use<SettingEntityDataService>()` NoAop 路径直建（`IEntityDAC<SettingEntity>` 从 DI 解析，走租户/软删链路）。**扩展/消费方均不得直接注入 IFreeSql / IEntityDAC / DataService**（数据访问红线 + DI004 零豁免）。

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`ISettingManager`** | 领域服务门面——分层读写管理器（V0.3.0 起 `: IDomainService`，消费方 `User.Use<ISettingManager>()`） | `SettingManager`（本扩展，继承 `DomainServiceBase`，`AddConstructibleService` 注册） |
| **`SettingEntityDataService`** | 设置表 DataService（SG1/xCodeGen——CRUD + `GetByKeyAsync`/`UpsertByKeyAsync` 等业务方法） | 内置，SG 消费方聚合自动注册（throw-factory） |
| **`ISettingManager`** | 分层读写管理器 | `SettingManager`（本扩展） |
| **`SettingEntity`** | 设置表实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`SettingsUserInfo`** | 扩展专用用户类型（继承 SimpleUserInfo） | 内置 |
| **`SettingsOptions`** | 配置选项（`TKWF:Settings` 节，含 `CacheExpirationSeconds`） | 内置 |
| **`SettingsExtensionInitializer`** | 扩展初始化器（三钩子） | 内置，`[TKWFExtension]` SG1 发现（能力清单）+ 消费方 `[TKWFEnabledExtension]` 白名单启用 |

---

## 五、实体表结构 (Entity Schema)

`SettingEntity` 映射到 `Setting` 表：

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| Name | NVARCHAR(256) | 设置名称 |
| Value | NVARCHAR(MAX) | 设置值（JSON 字符串） |
| ProviderName | NVARCHAR(128) | 提供者名称（Global/Tenant/User） |
| ProviderKey | NVARCHAR(128) | 提供者键（租户 ID / 用户 ID） |
| Description | NVARCHAR(512) | 设置描述 |
| IsVisibleToClients | BIT | 是否对客户端可见 |
| CreateTime | DATETIMEOFFSET | 创建时间 |
| UpdateTime | DATETIMEOFFSET | 更新时间 |

---

## 六、分层读取逻辑 (Layered Reading)

V0.2.0 实现完整分层：

```
读取顺序：User → Tenant → Global → 默认值
```

- **User 层**：`ProviderName = "User"`, `ProviderKey = userId`
- **Tenant 层**：`ProviderName = "Tenant"`, `ProviderKey = tenantId`
- **Global 层**：`ProviderName = "Global"`, `ProviderKey = null`

**匿名降级**：`IsAuthenticated == false` 时跳过 User/Tenant 层，直接查 Global → 默认值。

**写入层**：已认证用户写 User 层，匿名写 Global 层。

---

## 七、缓存策略 (Caching Strategy)

V0.2.0 引入 `IMemoryCache` 读缓存：

- **缓存 key**：`Setting:{ProviderName}:{ProviderKey}:{Name}`（如 `Setting:User:42:Theme`）
- **缓存过期**：`SettingsOptions.CacheExpirationSeconds` 默认 300 秒（5 分钟）
- **缓存失效**：`SetAsync` 后自动清除对应 key 的缓存
- **注册方式**：`TryAddSingleton<IMemoryCache, MemoryCache>`，消费方可覆盖

---

## 八、架构演进路线 (Architecture Roadmap)

### V0.1.0
- FreeSql 设置存储
- 基础 CRUD + Global 层读取
- 异常静默处理

### V0.2.0（当前）
- 完整分层（User → Tenant → Global → 默认值）
- 内存缓存（IMemoryCache + 过期策略 + 写后失效）
- Options 绑定修复（`[Options("TKWF:Settings")]` SG1 自动绑定 + `CacheExpirationSeconds`）
- 匿名降级（IsAuthenticated == false 跳过 User/Tenant 层）

### V0.3.0（规划）
- 管理 UI（设置编辑界面）
- 批量导入/导出
- 设置变更审计日志
