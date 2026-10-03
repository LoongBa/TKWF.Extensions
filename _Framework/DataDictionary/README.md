# TKWF.Ext.DataDictionary 数据字典扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.2.0 (缓存与树形分组 + VEntity JOIN 下推) + **V0.3.0（领域自治根治，ADR90）** | **框架**: .NET 10

**核心约束**: 字典定义+字典项双实体、按编码查询、FreeSql 持久化、异常静默处理、SG1 声明式实体、按 Code 内存缓存、树形分组、**VEntity 读模型联邦（JOIN 下推单查询）**

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

- **域作用域守卫（V0.3.0，ADR90）**：`IDictionaryStore` / `IDictionaryManager` 经 `AddConstructibleService` 注册——DI 中唯一可解析的是接口本身，且解析必须处于 `User.Use<T>()` 调用链内（`CurrentAopUser` 守卫）；实现类注册为 throw-factory（禁直接 DI 解析）。

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

自动注册（V0.3.0 领域自治根治，ADR90）：`IDictionaryStore`（默认 `DictionaryStore`，`AddConstructibleService` 接口守卫工厂）+
`IDictionaryManager`（默认 `DictionaryManager`，`AddConstructibleService` 接口守卫工厂）+ `IMemoryCache`（TryAddSingleton）+
`DataDictionaryOptions`（默认值，消费方自行绑定配置节）。消费方统一经 `User.Use<IDictionaryStore>()` / `User.Use<IDictionaryManager>()` 解析（AOP 路径），禁构造注入（DI004 零豁免）。

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
| **`IDictionaryStore`** | 字典存储抽象（CRUD + 按编码查询 + 按 Id 查询） | `DictionaryStore`（本扩展，继承 `DomainServiceBase` + `[DiContractIgnore]`，DataService 委托，数据访问红线整改 2026-09-07；V0.3.0 `AddConstructibleService` 注册） |
| **`IDictionaryManager`** | 数据字典管理门面（按编码聚合查询 + 缓存拦截 + 树形组装） | `DictionaryManager`（本扩展，继承 `DomainServiceBase` + `[DiContractIgnore]`，V0.3.0 `AddConstructibleService` 注册） |
| **`DictionaryDefinitionEntity`** | 字典定义实体（SG1 声明式） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`DictionaryItemEntity`** | 字典项实体（SG1 声明式，V0.2.0 含树形字段 ParentCode/Level/Path） | 内置，`partial class` + `[DomainGenerateCode]` |
| **`DictionaryDefinitionWithItems`** | 定义+项聚合返回（record） | 内置 |
| **`DictionaryTreeNode`** | 树形节点（V0.2.0，record：Code/DisplayName/Value/Order/IsEnabled/Children） | 内置 |
| **`DictionaryItemView`** | 字典项-定义 JOIN 视图实体（V0.2.0 VEntity，12 投影列——`ExposeGraphqlQuery=false` 树语义保护，经门面） | 内置，`partial class` + `[DomainGenerateCode(IsView=true)]` |
| **`DictionaryItemViewDataService`** | 视图只读 DataService（V0.2.0，`IEntityReadOnlyDAC` 红线合规——`GetByDefinitionCodeAsync` 单查询下推） | 内置，手写只读 DataService |
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

### V0.2.0（已发布）
- 内存缓存层（按 Code 缓存聚合，key=`DD:{Code}`，写入后按 Code 失效）
- 树形分组（`GetItemsTreeAsync` 递归组装嵌套树，`EnableTreeMode` 控制）
- `DictionaryItemEntity` 新增 ParentCode/Level/Path 三列（Position 10/11/12）
- `DataDictionaryOptions` 新增 EnableCache/CacheExpirationSeconds/EnableTreeMode
- **VEntity 化（JOIN 下推）**：新增 `vw_DictionaryItemView`（`DictionaryItem` INNER JOIN `DictionaryDefinition`，12 投影列含 `DefinitionCode`/`DefinitionDisplayName`）——`GetOrLoadAggregateAsync` 未命中路径两步骤一（定义单查 + 视图单查询项替代按 DefinitionId 查项）；**"定义存在但无项"语义保留**（先单查定义不存在→null；定义存在视图零行=空项列表，返回非 null 空聚合——oracle3 C-1/H1 方案 b）；BuildTree 留内存；缓存 key/写路径零触碰
- **⚠️ 生产部署**：`SyncViewsAsync` 仅开发环境建视图（`DisableSyncStructure=true`）——**生产需 DBA 手动执行 ViewSql**（见 `docs/DataDictionary/数据字典扩展-使用指南.md` § VEntity 化/下推）

### V0.3.0（已实施，领域自治根治 ADR90——V4.10.53 正确路线）
- 门面/Store 继承 `DomainServiceBase`（经基类 `User` 获取用户上下文——IDomainUser 永不注册 DI）+ `[DiContractIgnore]` 豁免 DI001
- 注册形态 `TryAddScoped` → `AddConstructibleService<IDictionaryStore, DictionaryStore>` / `<IDictionaryManager, DictionaryManager>`（接口可构造守卫工厂 + 实现类 throw-factory）
- 测试宿主重写走生产路径（真实 DI + `DomainUser<T>.BindScope` + `User.Use<接口>()` AOP；Initializer 注册形态断言守卫工厂/throw-factory/域外抛）

### V0.4.0（规划）
- 管理 UI
- 字典导入/导出
- 与 PrintTemplates 字段映射集成