# TKWF.Ext.DataDictionary 数据字典扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 (数据字典存储与查询) | **框架**: .NET 10

**核心约束**: 字典定义+字典项双实体、按编码查询、FreeSql 持久化、异常静默处理、SG1 声明式实体

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

- **字典项实体 (`DictionaryItemEntity`)**：归属某定义的具体选项——编码/显示名/值/排序/启用。

- **存储抽象 (`IDictionaryStore`)**：字典定义与项的 CRUD + 按编码查询。扩展提供 FreeSql 默认实现。

- **管理门面 (`IDictionaryManager`)**：屏蔽 Store 细节——按编码读取定义/项/完整集合（`GetDefinitionWithItemsAsync`），一次返回定义 + 排序后的项集合。

### 2. 安全语义

- **异常静默**：存储/管理操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。

- **TryAdd 语义**：DI 注册用 `TryAddScoped`——消费方自定义实现优先；扩展默认实现不覆盖消费方。

- **Scoped 生命周期**：`IDictionaryStore` / `IDictionaryManager` Scoped，自动参与当前请求上下文。

- **Upsert 幂等**：`UpsertDefinitionAsync` 按 Code 定位、`UpsertItemAsync` 按 DefinitionId+Code 定位——重复提交更新而非报错。

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

自动注册：`IDictionaryStore`（默认 `FreeSqlDictionaryStore`）+ `IDictionaryManager`（默认 `DictionaryManager`）。

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
```

### 4. 配置选项

通过 `appsettings.json` 配置：

```json
{
  "TKWF": {
    "DataDictionary": {
      "IsEnabled": true
    }
  }
}
```

### 5. 自定义 IDictionaryStore / IDictionaryManager

TryAdd 语义确保消费方实现优先；自定义实现同理。

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IDictionaryStore`** | 字典存储抽象（CRUD + 按编码查询） | `FreeSqlDictionaryStore`（本扩展） |
| **`IDictionaryManager`** | 数据字典管理门面（按编码聚合查询） | `DictionaryManager`（本扩展） |
| **`DictionaryDefinitionEntity`** | 字典定义实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`DictionaryItemEntity`** | 字典项实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`DictionaryDefinitionWithItems`** | 定义+项聚合返回（record） | 内置 |
| **`DataDictionaryUserInfo`** | 扩展专用用户类型（继承 SimpleUserInfo） | 内置 |
| **`DataDictionaryOptions`** | 配置选项（`TKWF:DataDictionary` 节） | 内置 |
| **`DataDictionaryExtensionInitializer`** | 扩展初始化器（三钩子） | 内置，`[TKWFExtension]` SG1 发现 |

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

`DictionaryItemEntity` → `DictionaryItem` 表：Id / DefinitionId（索引）/ Code / DisplayName / Value? / Order / IsEnabled / CreateTime / UpdateTime

---

## 六、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- 字典定义 + 字典项双实体 + FreeSql 存储
- 按编码聚合查询（`GetDefinitionWithItemsAsync`）+ Upsert 幂等
- 异常静默处理

### V0.2.0（规划）
- 内存缓存层（按 Definition Code 过期）
- 树形分组（多级字典项）

### V0.3.0（规划）
- 管理 UI
- 字典导入/导出
- 与 PrintTemplates 字段映射集成