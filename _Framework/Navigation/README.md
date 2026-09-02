# TKWF.Ext.Navigation 导航扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 (菜单数据模型与贡献机制) | **框架**: .NET 10

**核心约束**: 编译期贡献者发现、纯内存无持久化、权限过滤降级安全、TryAdd 可替换

---

## 一、需求分析 (Demand Analysis)

企业应用的后台导航菜单是几乎所有系统都需要的横切能力。主框架 `_TKWF` 菜单渲染/导航逻辑自 V4.9.74 起迁出为扩展，理由：

- **渲染与数据解耦**：菜单数据模型（树形定义）应与具体 UI 框架（Blazor/Razor/前端 SPA）解耦——框架只提供数据 + 权限过滤，不提供渲染。

- **权限过滤需求**：菜单项需按当前用户权限动态显示/隐藏（"用户看不到无权访问的菜单"）。

- **贡献分散**：各业务模块应有独立贡献自己菜单项的能力，而不必集中修改一处。

- **硬编码模块依赖**：若菜单集中在框架核心，每增加一个业务模块都要改框架代码。

---

## 二、设计原理 (Design Principles)

本扩展采用 **"编译期贡献者发现 + 纯内存模型 + 运行时权限过滤 + 降级安全"** 架构（对齐 D17 §5.2）。

### 1. 结构分层

- **菜单项模型（`MenuItemDefinition`）**：树形数据模型（`Parent` 层级关联），不含渲染信息——Name/DisplayName/Url/Icon/Order/Parent/RequiredPermissions/Logic/IsEnabled/IsVisible。

- **贡献契约（`IMenuContributor` + `MenuContributorAttribute`）**：业务模块实现 `ConfigureMenu(context)` 声明菜单项；`[MenuContributor]` 标记后由 SG1 编译期扫描发现并生成注册表（与 `[PermissionContributor]` 同构）。

- **配置上下文（`MenuConfigurationContext`）**：贡献者调用 `Add()` 声明菜单项（Name 必填且唯一，重复抛异常）。

- **定义仓库（`IMenuDefinitionRepository`）**：存放已收集的菜单定义（ConfigureServices 阶段静态收集，运行时只读）。

- **菜单管理器（`IMenuManager`）**：运行时按当前用户权限过滤 + 扁平排序 + 循环检测，返回 `MenuItemDefinition[]`。

- **扩展初始化器（`NavigationExtensionInitializer<TUserInfo>`）**：`[TKWFExtension("Navigation")]`，三钩子接线。

### 2. 安全语义

- **权限过滤（fail-visible 降级）**：`RequiredPermissions` 非空时调 `IPermissionChecker` 判定（`Logic=All` 全部授予显示 / `Any` 任一授予显示）；**checker 未注册 → 降级不过滤**（返回全菜单）。菜单是展示层数据非安全边界，安全由 `PermissionFilterAttribute` 兜底（ADR39）。

- **TryAdd 语义**：DI 注册用 `TryAddSingleton`——消费方自定义实现优先；扩展填充实例不覆盖消费方实现。

- **Singleton 生命周期**：`IMenuDefinitionRepository` 填充实例 + `IMenuManager` 均为 Singleton（菜单定义静态，权限过滤每次调用基于 ambient 当前用户）。

- **循环检测**：`Parent` 引用成环（A→B→A）抛明确异常，防无限递归。

### 3. 扩展间依赖

- **依赖倒置（ADR48 D7）**：本扩展引用 `TKWF.Ext.Permissions.Abstractions`（`IPermissionChecker`/`PermissionLogic`）——仅接口契约，不引用 `TKWF.Ext.Permissions` 实现项目（L2 门控 TKWF0022 校验）。

- **无持久化**：本扩展为纯内存服务（无 FreeSql 实体/表），菜单定义编译期收集、运行时只读。如需数据库存储菜单定义，可自定义 `IMenuDefinitionRepository`。

---

## 三、使用说明 (Usage Guide)

### 1. 宿主集成 (Hosting)

消费方引用 `TKWF.Ext.Navigation` 包，扩展经 `[TKWFExtension]` 被 SG1 编译期发现（生成能力清单）。**V4.9.85 起发现不自动启用**——消费方须在自身领域初始化器上声明白名单，三钩子才接线：

```csharp
using TKWF.Ext.Navigation;

[TKWFEnabledExtension(typeof(NavigationExtensionInitializer<>))]
public class XxxDomainInitializer : DomainHostInitializerBase<XxxUserInfo> { ... }
```

白名单声明后自动注册：`IMenuDefinitionRepository`（填充实例）+ `IMenuManager`（默认 `MenuManager<TUserInfo>`）。

### 2. 贡献菜单项（业务模块侧）

```csharp
[MenuContributor]
public class MainMenuContributor : IMenuContributor
{
    public void ConfigureMenu(MenuConfigurationContext context)
    {
        // 顶级菜单
        context.Add(new MenuItemDefinition
        {
            Name = "Orders",
            DisplayName = "订单",
            Url = "/orders",
            Icon = "orders",
            Order = 1,
            RequiredPermissions = new[] { "Order.View" }
        });

        // 子菜单（Parent 指向顶级 Name）
        context.Add(new MenuItemDefinition
        {
            Name = "Orders.Pending",
            DisplayName = "待处理订单",
            Url = "/orders/pending",
            Parent = "Orders",
            Order = 1
        });
    }
}
```

SG1 扫描 `[MenuContributor]` → 生成注册表 → 扩展初始化器在 ConfigureServices 阶段实例化并调用 `ConfigureMenu` 收集。

### 3. 读取菜单（渲染层侧）

```csharp
// 注入 IMenuManager
public class NavMenuViewComponent(IMenuManager menuManager)
{
    public async Task<IReadOnlyList<MenuItemDefinition>> GetMainMenuAsync()
    {
        // 自动按当前用户权限过滤；返回扁平数组（Parent 引用保留，UI 层据此建树）
        var items = await menuManager.GetMainMenuAsync();
        return items;
    }
}
```

**返回形态**：扁平 `MenuItemDefinition[]`，按 `(深度, Order)` 排序——顶层优先、父子相邻，便于 UI 层线性渲染建树。

### 4. 配置选项

通过 `appsettings.json` 配置：

```json
{
  "TKWF": {
    "Navigation": {
      "DefaultMenuName": "Main",
      "MaxMenuDepth": 4
    }
  }
}
```

亦可在 `ConfigureExtensions` 中 `services.Configure<NavigationOptions>(o => ...)` 覆盖。

### 5. 权限过滤行为

| RequiredPermissions | Logic | 行为 |
|---------------------|:-----:|------|
| `null` / 空 | — | 始终显示 |
| `["A","B"]` | `All`（默认） | 用户须同时拥有 A 和 B 才显示 |
| `["A","B"]` | `Any` | 用户拥有 A 或 B 即显示 |
| 任意 | — | **checker 未注册 → 降级不过滤**（返回全菜单） |

---

## 四、核心组件清单 (Component List)

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`IMenuManager`** | 菜单读取门面（权限过滤 + 排序 + 循环检测） | `MenuManager<TUserInfo>`（本扩展） |
| **`IMenuDefinitionRepository`** | 菜单定义仓库（只读） | `MenuDefinitionRepository`（本扩展，填充实例） |
| **`IMenuContributor`** | 菜单贡献契约（`ConfigureMenu` 同步声明） | 业务模块实现 + `[MenuContributor]` 标记 |
| **`MenuItemDefinition`** | 菜单项数据模型（树形，无渲染信息） | 内置 |
| **`MenuConfigurationContext`** | 贡献上下文（`Add()` 声明，Name 唯一校验） | 内置 |
| **`NavigationOptions`** | 配置选项（`TKWF:Navigation` 节） | 内置 |
| **`NavigationExtensionInitializer<TUserInfo>`** | 扩展初始化器（三钩子） | 内置，`[TKWFExtension("Navigation")]` SG1 发现 + 消费方白名单启用 |

---

## 五、数据模型 (Data Model)

`MenuItemDefinition` 字段：

| 字段 | 类型 | 说明 |
|------|------|------|
| Name | string | 菜单项唯一标识（层级关联用，必填） |
| DisplayName | string | 显示名（支持 i18n key） |
| Url | string? | 导航 URL（如 "/orders"） |
| Icon | string? | 图标标识（前端图标库 key） |
| Order | int | 同级排序（小值在前） |
| Parent | string? | 父菜单项 Name（顶层为 null） |
| RequiredPermissions | string[]? | 所需权限名列表（null/空 = 无限制） |
| Logic | PermissionLogic | 权限判定逻辑（默认 All） |
| IsEnabled | bool | 是否启用（false 不显示） |
| IsVisible | bool | 是否可见（false 不显示，UI 可保留占位） |

> 本扩展**无数据库表**——菜单定义编译期静态收集、运行时内存只读。

---

## 六、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- 菜单数据模型 + 贡献机制（`[MenuContributor]` + SG1 发现）
- 权限过滤（All/Any + checker 缺失降级不过滤）
- 扁平排序 + 循环检测
- 配置选项（`TKWF:Navigation`）

### V0.2.0（规划）
- 多菜单组分区（`DefaultMenuName` 生效，Main/Admin/Mobile 分组）
- 菜单持久化（FreeSql 存储自定义仓库示例）
- 菜单缓存（基于权限组合的缓存 key）

### V0.3.0（规划）
- 菜单项动态显隐（运行时按业务条件控制）
- 与 Tagging/DataDictionary 联动（菜单项标签过滤）
- 管理 UI