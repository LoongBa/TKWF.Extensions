# TKWF.Ext.Settings 设置管理扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 (设置管理与持久化) | **框架**: .NET 10

**核心约束**: 分层键值对存储、FreeSql 持久化、异常静默处理、SG1 声明式实体

---

## 一、需求分析 (Demand Analysis)

在领域驱动设计 (DDD) 的应用层中，常需对系统设置进行分层管理——不同租户/用户可能拥有不同的配置值，同时需要持久化到数据库。

- **配置分散**：appsettings.json 不支持运行时动态修改，且无法按用户/租户分层。

- **硬编码问题**：默认值散落在代码中，无法统一管理和审计。

- **持久化耦合**：设置存储直接依赖特定 ORM（FreeSql/EF Core），扩展无法做到 ORM 无关。

- **分层需求**：不同级别的设置（全局/租户/用户）需要不同的读取优先级。

---

## 二、设计原理 (Design Principles)

本扩展采用 **"存储抽象 + ORM 无关持久化 + 分层读取 + 异常静默"** 架构。

### 1. 结构分层

- **存储抽象 (`ISettingStore`)**：定义 CRUD 操作，按 Provider 定位设置（名称 + ProviderName + ProviderKey）。

- **持久化实现 (`FreeSqlSettingStore`)**：将设置映射为 `SettingEntity` 并持久化。异常静默处理（不阻塞业务）。

- **管理器 (`ISettingManager`)**：分层读写接口，屏蔽 Provider 细节，提供类型安全的 Get/Set。

- **管理器实现 (`SettingManager`)**：分层查找逻辑（User → Tenant → Global → 默认值），JSON 序列化支持。

- **声明式实体 (`SettingEntity`)**：SG1 化实体，`partial class` + `[DomainGenerateCode]`，FreeSql `[Column]` 特性。

### 2. 安全语义

- **异常静默**：读写失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。

- **TryAdd 语义**：DI 注册用 `TryAddScoped`——消费方自定义实现优先；扩展默认实现不覆盖消费方。

- **Scoped 生命周期**：`ISettingStore` / `ISettingManager` Scoped，自动参与当前请求上下文。

### 3. 与主框架的关系

- `IDomainUser` 由主框架定义（不动），用于获取当前用户/租户上下文。
- 本扩展提供 `FreeSqlSettingStore` + `SettingManager` 实现 + `SettingsExtensionInitializer` 注册。
- 消费方通过 `ISettingManager` 进行设置读写，无需关心 Provider 细节。

---

## 三、使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.Settings` 包，扩展经 `[TKWFExtension]` 被 SG1 编译期发现（生成能力清单）。**V4.9.85 起发现不自动启用**——消费方须在自身领域初始化器上声明白名单，三钩子才接线：

```csharp
[TKWFEnabledExtension(typeof(SettingsExtensionInitializer<>))]
public class XxxDomainInitializer : DomainHostInitializerBase<XxxUserInfo> { ... }
```

白名单声明后自动注册：`ISettingStore`（默认 `FreeSqlSettingStore`）+ `ISettingManager`（默认 `SettingManager`）。

### 2. 读写设置

```csharp
// 注入 ISettingManager
public class MyService(ISettingManager settingManager)
{
    public async Task<string> GetThemeAsync()
    {
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
      "IsEnabled": true
    }
  }
}
```

### 4. 自定义 ISettingStore

若需替换 `FreeSqlSettingStore`（如写文件、Redis）：

```csharp
// 消费方 ConfigureServices 中
services.AddScoped<ISettingStore, RedisSettingStore>();
```

TryAdd 语义确保消费方实现优先。

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`ISettingStore`** | 设置存储抽象（CRUD） | `FreeSqlSettingStore`（本扩展） |
| **`ISettingManager`** | 分层读写管理器 | `SettingManager`（本扩展） |
| **`SettingEntity`** | 设置表实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`SettingsUserInfo`** | 扩展专用用户类型（继承 SimpleUserInfo） | 内置 |
| **`SettingsOptions`** | 配置选项（`TKWF:Settings` 节） | 内置 |
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

V0.1.0 仅实现 Global 层；V0.2.0 将实现完整分层：

```
读取顺序：User → Tenant → Global → 默认值
```

- **User 层**：`ProviderName = "User"`, `ProviderKey = userId`
- **Tenant 层**：`ProviderName = "Tenant"`, `ProviderKey = tenantId`
- **Global 层**：`ProviderName = "Global"`, `ProviderKey = null`

---

## 七、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- FreeSql 设置存储
- 基础 CRUD + 分层读取
- 异常静默处理

### V0.2.0（规划）
- 完整分层（User → Tenant → Global）
- 缓存层（内存缓存 + 过期策略）
- EF Core 存储实现

### V0.3.0（规划）
- 管理 UI（设置编辑界面）
- 批量导入/导出
- 设置变更审计日志
