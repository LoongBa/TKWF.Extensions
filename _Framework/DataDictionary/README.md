# TKWF.Ext.DataDictionary 数据字典扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.2.0 (缓存与树形分组) | **框架**: .NET 10

**核心约束**: 字典定义+字典项双实体、按编码查询、FreeSql 持久化、异常静默处理、SG1 声明式实体、按 Code 内存缓存、树形分组

---

## 一、需求分析 (Demand Analysis)

数据字典是业务系统最通用的横切能力——枚举/下拉/参考数据集中管理（性别、状态、类型、国家地区等）。主框架 `_TKWF` **完全缺失**（零实体/零服务），对标清单标记为 **TKWF 差异化 P0**。

- **枚举散落**：业务枚举硬编码在代码中，无法运行时维护。
- **下拉重复**：各模块自行维护下拉选项，无统一来源。
- **参考数据分散**：国家/地区/货币等参考数据无集中管理。

---

## 二、设计原理 (Design Principles)

本扩展采用 **"双实体聚合 + 按编码查询 + FreeSql 持久化 + 异常静默"** 架构。

### 1. 结构分层

- **字典定义实体 (`DictionaryDefinitionEntity`)**：字典聚合根——编码/名称/描述/启用。

- **字典项实体 (`DictionaryItemEntity`)**：归属某定义的具体选项——编码/显示名/值/排序/启用 + 树形三字段（ParentCode/Level/Path）。

- **存储抽象 (`IDictionaryStore`)**：字典定义与项的 CRUD + 按编码/按 Id 查询（V0.2.0 按 Id 查询供缓存失效反查）。扩展提供 FreeSql 默认实现。

- **管理门面 (`IDictionaryManager`)**：屏蔽 Store 细节——按编码读取定义/项/完整集合（`GetDefinitionWithItemsAsync`），一次返回定义 + 排序后的项集合。V0.2.0 新增 `GetItemsTreeAsync`（树形组装）与删除门面（`DeleteDefinitionAsync`/`DeleteItemAsync`，删除后失效缓存）。

- **缓存层 (V0.2.0, D4/D5)**：`DictionaryManager` 注入 `IMemoryCache` + `IOptions<DataDictionaryOptions>`；读取方法缓存拦截（key=`DD:{Code}` 存储 `DictionaryDefinitionWithItems` 聚合），写入方法后按 Code 失效（D6：`DeleteItem` 先反查 DefinitionId → 再查 Code）。

- **树形分组 (V0.2.0, D3)**：Store 层不改——复用 `GetItemsAsync(definitionId)` 平铺列表，Manager 内存递归组装 `DictionaryTreeNode` 树；`ParentCode` 为空或指向不存在父项的字典项自动归根。

### 2. 安全语义

- **异常静默**：存储/管理操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。

- **TryAdd 语义**：DI 注册用 `TryAddScoped`——消费方自定义实现优先；扩展默认实现不覆盖消费方。`IMemoryCache` 经 `AddMemoryCache()` 注册（内部 TryAddSingleton，D8，消费方可覆盖）。

- **Scoped 生命周期**：`IDictionaryStore` / `IDictionaryManager` Scoped，自动参与当前请求上下文；`IMemoryCache` Singleton。

- **Upsert 幂等**：`UpsertDefinitionAsync` 按 Code 定位、`UpsertItemAsync` 按 DefinitionId+Code 定位——重复提交更新而非报错。

- **缓存一致性 (D5/D6)**：失效粒度=整个字典定义；任一项变更即刷新该定义下所有缓存；`DeleteItem` 反查失败时由缓存过期兜底（默认 300s）。

---

## 三、使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.DataDictionary` 包，扩展经 `[TKWFExtension]` 被 SG1 发现；**V4.9.85 起发现不自动启用**——消费方须在领域初始化器上声明 `[TKWFEnabledExtension]` 白名单，三钩子才接线：

```csharp
// 消费方领域初始化器
using TKWF.Ext.DataDictionary;

[TKWFEnabledExtension(typeof(DataDictionaryExtensionInitializer<>))]
public class MyDomainInitializer : DomainHostInitializerBase<MyUserInfo>
{
    // ...
}
```

自动注册：`IDictionaryStore`（默认 `FreeSqlDictionaryStore`）+ `IDictionaryManager`（默认 `DictionaryManager`）+ `IMemoryCache`（TryAddSingleton）+ `DataDictionaryOptions`（默认值，消费方自行绑定配置节）。

### 2. 读取字典（业务侧）

```csharp
// 注入 IDictionaryManager
public class OrderFormService(IDictionaryManager dictionaryManager)
{
    // 一次返回完整字典（定义 + 排序后的项）
    public async Task<DictionaryDefinitionWithItems?> GetGenderOptionsAsync()
        => await dictionaryManager.GetDefinitionWithItemsAsync("Gender");

    // 仅取项列表
    public async Task<IReadOnlyList<DictionaryItemEntity>> GetStatusItemsAsync()
        => await dictionaryManager.GetItemsAsync("OrderStatus");
}
```

### 3. 维护字典（管理侧）

```csharp
// 新增/更新字典定义（按 Code 幂等）
await dictionaryManager.UpsertDefinitionAsync(new DictionaryDefinitionEntity
{
    Code = "Gender",
    DisplayName = "性别",
    Description = "用户性别选项"
});

// 新增/更新字典项（按 DefinitionId + Code 幂等）
await dictionaryManager.UpsertItemAsync(new DictionaryItemEntity
{
    DefinitionId = genderDef.Id,
    Code = "Male",
    DisplayName = "男",
    Order = 1
});

// 删除（V0.2.0 删除门面，删除后自动失效缓存）
await dictionaryManager.DeleteItemAsync(itemId);
await dictionaryManager.DeleteDefinitionAsync(defId); // 级联清理其项
```

### 4. 树形查询（V0.2.0）

```csharp
// 读取树形字典（需 EnableTreeMode=true；否则降级为平铺列表，Children 为空）
public async Task<IReadOnlyList<DictionaryTreeNode>> GetRegionTreeAsync()
    => await dictionaryManager.GetItemsTreeAsync("Region");
```

### 5. 配置选项

消费方在自身 `ConfigureServices` 中绑定配置节：

```csharp
services.Configure<DataDictionaryOptions>(configuration.GetSection("TKWF:DataDictionary"));
```

```json
{
  "TKWF": {
    "DataDictionary": {
      "IsEnabled": true,
      "EnableCache": true,
      "CacheExpirationSeconds": 300,
      "EnableTreeMode": false
    }
  }
}
```

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `IsEnabled` | `true` | 是否启用数据字典 |
| `EnableCache` | `true` | 是否启用按 Code 内存缓存；关闭后每次查库 |
| `CacheExpirationSeconds` | `300` | 缓存过期时间（秒），仅 `EnableCache=true` 时生效 |
| `EnableTreeMode` | `false` | 是否启用树形模式；`false` 时 `GetItemsTreeAsync` 降级为平铺列表 |

### 6. 自定义 IDictionaryStore / IDictionaryManager / IMemoryCache

TryAdd 语义确保消费方实现优先；自定义实现同理。

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IDictionaryStore`** | 字典存储抽象（CRUD + 按编码查询 + 按 Id 查询） | `FreeSqlDictionaryStore`（本扩展） |
| **`IDictionaryManager`** | 数据字典管理门面（按编码聚合查询 + 缓存拦截 + 树形组装） | `DictionaryManager`（本扩展） |
| **`DictionaryDefinitionEntity`** | 字典定义实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`DictionaryItemEntity`** | 字典项实体（SG1 声明式，V0.2.0 含树形字段 ParentCode/Level/Path） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`DictionaryDefinitionWithItems`** | 定义+项聚合返回（record） | 内置 |
| **`DictionaryTreeNode`** | 树形节点（V0.2.0，record：Code/DisplayName/Value/Order/IsEnabled/Children） | 内置 |
| **`DataDictionaryUserInfo`** | 扩展专用用户类型（继承 SimpleUserInfo） | 内置 |
| **`DataDictionaryOptions`** | 配置选项（`TKWF:DataDictionary` 节，V0.2.0 含 EnableCache/CacheExpirationSeconds/EnableTreeMode） | 内置 |
| **`DataDictionaryExtensionInitializer`** | 扩展初始化器（三钩子，V0.2.0 含 IMemoryCache + Options 绑定） | 内置，`[TKWFExtension]` SG1 发现 |

---

## 五、实体表结构 (Entity Schema)

`DictionaryDefinitionEntity` → `DictionaryDefinition` 表：

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| Code | NVARCHAR(128) | 字典编码（唯一，如 "Gender"） |
| DisplayName | NVARCHAR(128) | 显示名 |
| Description | NVARCHAR(512)? | 描述 |
| IsEnabled | BIT | 是否启用 |
| CreateTime | DATETIMEOFFSET | 创建时间 |
| UpdateTime | DATETIMEOFFSET | 更新时间 |

`DictionaryItemEntity` → `DictionaryItem` 表：

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| DefinitionId | BIGINT (索引) | 所属字典定义 ID |
| Code | NVARCHAR(128) | 字典项编码 |
| DisplayName | NVARCHAR(128) | 显示名 |
| Value | NVARCHAR(256)? | 关联值 |
| Order | INT | 排序（小值在前） |
| IsEnabled | BIT | 是否启用 |
| CreateTime | DATETIMEOFFSET | 创建时间 |
| UpdateTime | DATETIMEOFFSET | 更新时间 |
| ParentCode | NVARCHAR(128)? | 父项编码（V0.2.0，根节点为 null） |
| Level | INT | 层级深度（V0.2.0，根节点为 0） |
| Path | NVARCHAR(1024) | 物化路径（V0.2.0，形如 `/root/child`） |

---

## 六、架构演进路线 (Architecture Roadmap)

### V0.1.0（已发布）
- 字典定义 + 字典项双实体 + FreeSql 存储
- 按编码聚合查询（`GetDefinitionWithItemsAsync`）+ Upsert 幂等
- 异常静默处理

### V0.2.0（当前）
- 内存缓存层（按 Code 缓存聚合，key=`DD:{Code}`，写入后按 Code 失效）
- 树形分组（`GetItemsTreeAsync` 递归组装嵌套树，`EnableTreeMode` 控制）
- `DictionaryItemEntity` 新增 ParentCode/Level/Path 三列（Position 10/11/12）
- `DataDictionaryOptions` 新增 EnableCache/CacheExpirationSeconds/EnableTreeMode

### V0.3.0（规划）
- 管理 UI
- 字典导入/导出
- 与 PrintTemplates 字段映射集成