# ADR-Navigation-MenuContributorAttribute移除与契约归属收敛

> **版本**：V0.2.0（目标）
> **状态**：📋 提议（修订 1——Oracle 评审 PASS WITH CONDITIONS 条件已吸收，待框架组确认）
> **关联**：ADR48（依赖倒置 D7）、ADR61（SG 基类类型判定 + DataService 自动注册）、ADR50（三层门控）、v4.9.74（扩展机制业务模块 W2/W5）、V5前收尾清单 #12、ADR《扩展数据访问红线》
> **关键字**：Navigation、MenuContributor、SG1 字符串常量耦合、契约归属、Abstractions、V5 前破坏性变更消除

---

## 目的与目标

消除主框架 SG1 对扩展侧 `MenuContributorAttribute` 的**字符串常量跨程序集耦合**（`FrameworkTypes.MenuContributorAttribute = "TKWF.Ext.Navigation.MenuContributorAttribute"`），并把 `MenuContributorData` 元数据契约下沉到扩展侧 Navigation.Abstractions——使 Navigation 扩展的契约归属与 Permissions/Emailing/BlobStoring/Account 先例一致（ADR48 D7 依赖倒置），主框架不再以字符串硬编码引用扩展类型。

目标状态（3 句内）：①`MenuContributorAttribute` 从扩展侧移除，SG1 不再经字符串常量识别它——菜单贡献者发现改为对齐 ADR61 的**接口/基类类型判定**（`IMenuContributor` 语义）；②`MenuContributorData` 移入扩展侧 `TKWF.Ext.Navigation.Abstractions`，主框架 `CodeGeneration.Abstractions` 不再承载扩展专属元数据；③扩展仓库 Navigation 配套改造（新增 Abstractions 项目 + 依赖改向），消费方零或极小迁移。

---

## 问题

### 问题现象

1. **主框架 SG1 字符串常量引用扩展类型**：`_Domain.SG/CodeGeneration.Abstractions/FrameworkTypes.cs` L217 硬编码
   `MenuContributorAttribute = "TKWF.Ext.Navigation.MenuContributorAttribute"`——主框架编译期经 `GetTypeByMetadataName` 查找扩展侧特性。此模式与 `PermissionContributorAttribute`/`FeatureContributorAttribute` 同构（三者均为"主框架知悉扩展类型全名"的反向耦合）。
2. **`MenuContributorData` 归属主框架**：`CodeGeneration.Abstractions/Metadata/MenuContributorData.cs`——SG1 生成 `ProjectMetaContext.MenuContributors` override 与 `GeneratedMenuContributors.DescriptorData` 均引用 `global::TKW.Framework.CodeGeneration.MenuContributorData`。该类型是 Navigation 扩展专属契约（含 `Type ContributorType`），却寄居主框架 CodeGeneration 程序集。
3. **扩展侧无 Abstractions 项目**：Navigation 直接定义 `MenuContributorAttribute` + `IMenuContributor` + `MenuItemDefinition` + `MenuConfigurationContext` 于实现项目——无契约包（对比 Permissions/Emailing/BlobStoring/Account 均有 `.Abstractions`）。

### 触发场景

- 主框架演进：`FrameworkTypes` 每次新增扩展特性常量 = 主框架知悉扩展内部类型（D17 扩展机制基座的耦合面持续扩大）。
- 契约归属不一致：Navigation 是 P0 扩展中唯一无 Abstractions 的持久化无关模块；未来多菜单分区（V0.2.0）或消费方自定义仓库时契约无处安放。
- V5 破坏性变更消除：字符串耦合 + 元数据归属是"V5 后契约封死即固化"的欠账（对齐 #23 ExtractAsync 移除先例的用户裁定：V5 前消除破坏性变更）。

### 现有方案不足

- **`[MenuContributor]` 标记特性**（当前机制）：`MenuContributorAttribute` 是**无载荷纯标记**（`AttributeUsage(Class)`，空类）——SG1 仅靠它的类型存在来识别贡献者类。这本质是"用特性名当标志位"：若实现 `IMenuContributor` 的类漏标特性，SG1 静默漏收（无诊断）；特性本身无任何配置载荷，标记价值趋近于零，却制造了字符串耦合。
- **`MenuContributorData` 留主框架**：类型属于 Navigation 契约，留在 CodeGeneration 使主框架对扩展元数据产生编译期依赖——扩展契约应随扩展发布（对齐 `PermissionContributorData` 的同类问题，但 Permission 已有 Abstractions 承载其余契约，Navigation 则完全无）。

---

## 使用场景

1. **消费方贡献菜单**（常态）：实现 `IMenuContributor`（同步 `ConfigureMenu`）即被 SG1 自动发现——**不再需要 `[MenuContributor]` 标记**（接口实现本身就是意图声明，对齐 ADR61"基类类型判定"哲学与 `IDomainDataService`/`IDomainEntity` 先例）。
2. **扩展开发**（Navigation 演进）：契约（`IMenuContributor`/`MenuItemDefinition`/`MenuConfigurationContext`/`MenuContributorData`）集中于 `TKWF.Ext.Navigation.Abstractions`，实现项目引契约——后续多菜单分区、自定义 `IMenuDefinitionRepository` 均在此基础上演进。
3. **不适用**：`PermissionContributorAttribute`/`FeatureContributorAttribute` 的移除**不在本 ADR 范围**——二者与本 ADR 同构问题，但各自有独立语义（`[PermissionContributor]` 收集权限定义、`[FeatureContributor]` 收集功能定义），需单独评估（本 ADR 仅收敛 Navigation）。

---

## 决策

### 核心机制（对齐 ADR61 类型判定 + ADR48 D7 契约归属）

```
① SG1 发现改接口判定（Q1）── GetTypeByMetadataName 查 IMenuContributor 接口 → 收集实现类
        │ 不再查 MenuContributorAttribute 字符串常量
        ▼
② MenuContributorData 移入扩展侧 Abstractions ── Navigation.Abstractions 承载菜单契约全套
        │ 主框架 CodeGeneration.Abstractions 移除 MenuContributorData（+ 相关生成代码引用改向）
        ▼
③ 扩展侧新建 Navigation.Abstractions ── IMenuContributor/MenuItemDefinition/MenuConfigurationContext/MenuContributorData
        │ 实现项目引契约包；消费方实现 IMenuContributor 即自动发现
        ▼
字符串耦合消除 + 契约归属收敛 ✓
```

### 1. SG1 菜单贡献者发现改接口判定（主框架 `_Domain.SG`）

> **对齐先例**：ADR61 `InheritsFromDomainDataServiceBase`（基类名遍历判定）+ `ReferencesBackgroundJobs`（L1112 `ReferencedAssemblySymbols.Any(a => a.Name == ...)` 直接引用检测）。

- `FrameworkTypes`：移除 `MenuContributorAttribute` 常量；**新增** `IMenuContributor = "TKWF.Ext.Navigation.IMenuContributor"`（接口全名——命名空间保持，对齐方案 B）。
  - **契约归属裁定**：接口是扩展公开契约（非内部实现细节），主框架经 `FrameworkTypes` 常量知晓其全名合法——对齐 `FrameworkTypes` 已有 `IExtensionEntityRegistrar`/`ProjectMetaContextBase` 等契约类常量先例。
- `CollectFeatureGates`（L1104）：`menuAttr` 改为解析 `IMenuContributor` 接口符号；`CollectInNamespace` 内收集逻辑从"查 `[MenuContributor]` 特性"改为"查实现 `IMenuContributor` 的类"（Roslyn `INamedTypeSymbol.AllInterfaces`——含基类继承的接口，与 ADR61 `BaseType` 遍历语义一致）。
  - 收集语义：实现 `IMenuContributor` 的**非抽象类**即为菜单贡献者（对齐 ADR61 `IsCandidateClass` 放宽 + 抽象排除）；显式 `[DomainIgnore]` opt-out 仍生效（ADR61 语义）。
  - 不再需要 `[MenuContributor]` 特性——**接口实现即意图声明**（编译器强制实现 `ConfigureMenu`，消除漏标静默漏收窗口）。
- `MenuContributorRecord` 生成不变（FullName/Name）；`BuildMenuContributorsSource` 生成的 `DescriptorData` 引用改向扩展侧 `MenuContributorData`（见决策 2 接口化方案）。

> **破坏性边界确认**：SG1 生成代码在主项目编译，主项目**引用扩展 Abstractions 与否决定是否生成**——`CollectFeatureGates` 在无 Navigation 引用时 `IMenuContributor` 解析为 null → 不收集 → 生成空 override（消费方零感知，对齐现状）。有引用时生成 `DescriptorData` 引用扩展侧类型——编译期引用存在（Navigation 传递引用 Navigation.Abstractions），无缺类型风险。

### 2. `MenuContributorData` 移入扩展侧 Abstractions + 接口化解耦（Oracle 评审定案）

> **Oracle 评审（bg_2c3b5f62）PASS WITH CONDITIONS——核查清单 #1 定案为接口化方案 A'**：原选项 C（属性从基类移除、扩展直读 `GeneratedMenuContributors`）存在跨程序集访问 internal 生成物的实现通道难题（IVT 依赖消费方程序集名脆弱 / public 化扩大表面），否决。改用**接口解耦**方案：

- **新建** `_TKWF.Extensions/_Framework/Navigation.Abstractions/TKWF.Ext.Navigation.Abstractions.csproj`（net10.0）。
- **主框架 `CodeGeneration.Abstractions` 新增通用接口** `IContributorDescriptor`：
  ```csharp
  public interface IContributorDescriptor
  {
      string FullName { get; }
      string Name { get; }
      Type ContributorType { get; }
  }
  ```
  三个贡献者数据类型（`PermissionContributorData`/`MenuContributorData`/`FeatureContributorData`）均实现此接口——三者结构完全相同（FullName/Name/ContributorType 三元组，已核实）。
- **`MenuContributorData` 移入 Navigation.Abstractions**：从主框架 `CodeGeneration.Abstractions/Metadata/` 迁出，命名空间保持 `TKWF.Ext.Navigation`（方案 B），`class MenuContributorData : IContributorDescriptor`。
- **`ProjectMetaContextBase.MenuContributors` 属性改返回接口列表**：
  ```csharp
  public virtual IReadOnlyList<IContributorDescriptor> MenuContributors => Array.Empty<IContributorDescriptor>();
  ```
  → 主框架基类不再引用扩展专属类型（仅引用自身 `CodeGeneration.Abstractions` 内的接口）；SG 生成代码 override 返回 `MenuContributorData[]`（协变，元素实现接口可赋值）；Navigation 扩展 Initializer 读取时**无需强转**——接口已暴露 `ContributorType`/`FullName`（`Activator.CreateInstance(descriptor.ContributorType)` 即可，无需 cast 回具体类型）。
- **契约类型迁入**（命名空间保持 `TKWF.Ext.Navigation`）：
  - `IMenuContributor` + `MenuItemDefinition` + `MenuConfigurationContext`（从 Navigation 实现项目迁入，对齐 Account.Abstractions 先例：`IAccountPasswordManager` 移 Abstractions 但命名空间保持 `TKWF.Ext.Account`）
  - `MenuContributorAttribute` **删除**（不再需要）
- **主框架清理**：`CodeGeneration.Abstractions/Metadata/MenuContributorData.cs` 删除（`MenuContributorData` 移扩展侧）；`FrameworkTypes.MenuContributorAttribute` 常量删除、新增 `IMenuContributor` 常量。
- **依赖矩阵（Oracle 条件 3——修正"纯 BCL 契约包"表述）**：
  ```
  Navigation.Abstractions → Permissions.Abstractions   （MenuItemDefinition 使用 PermissionLogic）
  Navigation.Abstractions → TKWF.CodeGeneration.Abstractions（IContributorDescriptor 接口所在程序集）
  Navigation             → Navigation.Abstractions     （实现项目引契约包）
  ```

### 3. 扩展侧 Navigation 改造（`_TKWF.Extensions`）

- `TKWF.Ext.Navigation` csproj：新增 `ProjectReference → Navigation.Abstractions`；删除 `MenuContributorAttribute.cs`；`IMenuContributor`/`MenuItemDefinition`/`MenuConfigurationContext` 移除（移入 Abstractions）。
- `NavigationExtensionInitializer`：`ProjectMetaContextBase.Instance.MenuContributors` 返回类型改接口列表（`IContributorDescriptor`）——读取 `ContributorType` 实例化贡献者的逻辑不变（接口已暴露所需字段，无需强转）。
- **命名空间策略（Oracle 确认方案 B）**：契约类型移入 Abstractions 但**命名空间保持 `TKWF.Ext.Navigation`**（对齐 Account.Abstractions 先例：`IAccountPasswordManager` 移 Abstractions 但命名空间保持 `TKWF.Ext.Account`）——消费方 `using TKWF.Ext.Navigation` 引用 `IMenuContributor`/`MenuItemDefinition` 的代码**零编译破坏**，仅程序集分割（引用经依赖传递自动满足）。

### 4. 破坏性声明

- **消费方视角**：命名空间保持（方案 B）→ **零编译破坏**（`using` 不变，类型引用经 Navigation 传递引用 Navigation.Abstractions 自动解析）；`MenuContributorAttribute` 移除 → 消费方若标注了 `[MenuContributor]` 需删除该特性标注（**编译破坏**，但仅影响标注者，迁移机械：删特性 + 保接口实现）。
- **二进制视角**：`MenuContributorData`/`IMenuContributor` 等类型程序集归属变化（`TKWF.Ext.Navigation` → `TKWF.Ext.Navigation.Abstractions`）——直接 `typeof`/反射引用旧程序集类型者二进制破坏（罕见，v0.x 阶段可接受）。Type forwarding 不采用（对齐 Account.Abstractions 先例，v0.x 直接改引）。
- **主框架视角**：`ProjectMetaContextBase.MenuContributors` 返回类型改 `IReadOnlyList<IContributorDescriptor>`（主框架自身接口，无跨程序集问题）；`CodeGeneration.Abstractions` 移除 `MenuContributorData` 具体类。主框架 slnx 不引 Navigation 扩展——需核查主框架内 `MenuContributorData` 残余引用（见【后果】核查清单）。

---

## 备选方案

### A：MenuContributorData 保留主框架，仅移除 Attribute
- 只做 SG1 接口判定 + 删特性，`MenuContributorData` 留 CodeGeneration——解决字符串耦合的一半，但契约归属问题未收敛（元数据仍寄居主框架）。
- **否决**：目标 ② 未达成；与 Permissions 先例（契约随扩展）不一致。

### B：Attribute 保留，仅新增 Abstractions 承载契约
- 不删 `[MenuContributor]`，仅把契约移 Abstractions——字符串耦合仍在（`FrameworkTypes` 常量改指向 Abstractions 内特性）。
- **否决**：特性纯标记无载荷，保留即保留"漏标静默漏收"缺陷 + 字符串耦合；接口判定（决策 1）成本更低且对齐 ADR61 方向。

### C：MenuContributor 机制整体迁移消费方（SG 不感知）
- SG1 完全移除 MenuContributor 收集，改由 Navigation 扩展 Initializer 运行时反射扫描 `IMenuContributor` 实现。
- **否决**：运行时反射违背 ADR48"编译期强类型、零反射"（D4）；且消费方编译期能力清单（ADR47 权威注册源）会缺失菜单贡献者信息。

---

## 后果

### 正面
- 主框架不再字符串耦合 Navigation 特性（`FrameworkTypes` 瘦身 1 常量）；`CodeGeneration.Abstractions` 不再承载扩展专属元数据。
- Navigation 契约归属与 Permissions/Emailing/BlobStoring/Account 一致（ADR48 D7 依赖倒置 + 契约随扩展）。
- `[MenuContributor]` 漏标静默漏收缺陷消除——实现 `IMenuContributor` 即自动发现（接口语义，无标记遗漏窗口）。
- V5 前破坏性变更消除（对齐 #23 ExtractAsync 先例用户裁定）。

### 负面
- 消费方标注 `[MenuContributor]` 的代码需删特性（编译破坏，机械迁移）。
- 主框架 `ProjectMetaContextBase.MenuContributors` 返回类型归属问题需解决（见下）。
- `MenuContributorData` 迁移触发主框架 SG 生成代码 + `ProjectMetaContextBase` 属性签名联动修改（高影响面，需回归）。

### 核查清单（Oracle 评审定案后）
1. **`ProjectMetaContextBase.MenuContributors` 属性**——**已定案（Oracle bg_2c3b5f62）**：改返回 `IReadOnlyList<IContributorDescriptor>`（主框架 `CodeGeneration.Abstractions` 新增通用接口，三贡献者数据类型统一实现）——主框架基类仅引用自身接口，无跨程序集问题；扩展侧读接口字段即够（无需强转）。
2. **主框架内 `MenuContributorData` 残余引用扫描**：`_Domain.SG` 生成代码 + `CodeGeneration.Abstractions` 模板（含 `ProjectMetaContextBase` L78）——改为引用 `IContributorDescriptor` 或扩展侧类型；`PermissionContributorData`/`FeatureContributors` 仍引用 `PermissionContributorData`（本 ADR 范围外，不变）。
3. **`PermissionContributorData`/`FeatureContributorData` 同构问题**：本 ADR 不动，另立评估（避免范围膨胀）。⚠️ 事实约束：`PermissionContributors`（L69）与 `FeatureContributors`（L88）**共享 `PermissionContributorData` 单一类型**——本 ADR 新增 `IContributorDescriptor` 接口后，二者**可选**未来迁移至接口（低风险、纯增强），但不构成本 ADR 承诺。
4. **`MenuItemDefinition` 的 `PermissionLogic` 依赖**（Oracle 条件 3）：Navigation.Abstractions 须 `ProjectReference → Permissions.Abstractions`——已列入决策 2 依赖矩阵。

### 迁移路径（Oracle 条件 5——含测试侧改造；2026-09-17 落档细化）

> **依赖顺序**：主框架先（v4.10.28 发包 + 部署根同步）→ 扩展侧后（DLL 模式需新 DLL）。

**步骤 1：主框架 `_TKWF`（SG1 改造 + 接口新增）——框架组权限与工作**

| # | 文件 | 改动 |
|---|------|------|
| 1a | `_Domain.SG/CodeGeneration.Abstractions/FrameworkTypes.cs` | 删 `MenuContributorAttribute`（L217）；增 `MenuContributorInterface = "TKWF.Ext.Navigation.IMenuContributor"` |
| 1b | `_Domain.SG/CodeGeneration.Abstractions/Metadata/` | 新增 `IContributorDescriptor` 接口（FullName/Name/ContributorType 三成员） |
| 1c | `_Domain.SG/CodeGeneration.Abstractions/Metadata/MenuContributorData.cs` | 删除（迁扩展侧） |
| 1d | `_Domain.SG/CodeGeneration.Abstractions/Metadata/ProjectMetaContextBase.cs` | `MenuContributors` 返回类型 → `IReadOnlyList<IContributorDescriptor>`（L78） |
| 1e | `_Domain.SG/CodeGeneration/EntityMetadataGenerator.Generation.cs` | `CollectFeatureGates`（L1104）`menuAttr` 改解析接口；`CollectInNamespace` 收集改 `AllInterfaces` 判定；`BuildMenuContributorsSource` 引用改扩展侧类型 |

主框架回归：`Domain.SG.Tests` 专项 + slnx 全量 0 警告 0 错误。

**步骤 2：扩展仓库 `_TKWF.Extensions`（契约归位）**

| # | 文件 | 改动 |
|---|------|------|
| 2a | `_Framework/Navigation.Abstractions/TKWF.Ext.Navigation.Abstractions.csproj`（新建） | 契约包（PackageReference CodeGeneration.Abstractions + ProjectReference Permissions.Abstractions + MinVerTagPrefix `Navigation.Abstractions/v`） |
| 2b | `Navigation.Abstractions/` 迁入 | `IMenuContributor`/`MenuItemDefinition`/`MenuConfigurationContext`/`MenuContributorData`（`: IContributorDescriptor`）命名空间保持 `TKWF.Ext.Navigation` |
| 2c | `Navigation/MenuContributorAttribute.cs` | 删除 |
| 2d | `Navigation/TKWF.Ext.Navigation.csproj` | 增 ProjectReference → Navigation.Abstractions |
| 2e | `Navigation/NavigationExtensionInitializer.cs` | 读取改 `IReadOnlyList<IContributorDescriptor>` |

**步骤 3：测试 + 文档**

| # | 文件 | 改动 |
|---|------|------|
| 3a | `Navigation.Tests/NavigationExtensionInitializerTests.cs` | `FakeMetaContext.MenuContributors` 改 `IContributorDescriptor[]`；`MainMenuContributor` 删 `[MenuContributor]` |
| 3b | `docs/Navigation/导航扩展-使用指南.md` | 贡献菜单示例删特性，改纯接口 |
| 3c | 主框架 `G17B` | 菜单贡献者用法同步 |

回归门禁：扩展 Navigation 29/29（含多菜单）+ 全量 slnx。

---

## 五、范围讨论（2026-09-17 用户提出，待框架组裁定）

> **架构本质**：本 ADR 当前仅处理 Navigation 的 MenuContributor——但勘察证明 **Permission/Menu/Feature 三套贡献者机制完全同构**（FrameworkTypes 3 字符串常量 + ProjectMetaContextBase 3 属性，机制逐一复制）。只修 Menu 是**局部修补**，深层问题是"扩展机制贡献者发现"的基础设计。

**讨论问题**（详见 `docs/Navigation/ADR/ADR-Navigation-贡献者发现机制范围讨论.md`）：
1. **范围**：是否不局限于 Menu——三套同构机制（Permission/Menu/Feature）是否统一收敛？
2. **归属**：贡献者发现是否属于扩展机制基础（SG 基座/D17 层）而非各扩展自理？——ADR61 已确立"基类类型判定"先例，接口判定同向。
3. **更好设计**：框架组是否有统一方案（如通用贡献者声明机制）？

**裁定前**：本 ADR 决策 1-4 的工作分解保持"仅 Menu"最小范围可实施；若裁定统一收敛，本 ADR 升级为"扩展机制贡献者发现统一"并扩大工作分解。

### 回归门禁
- 主框架：Domain.SG.Tests（收集逻辑改动专项）+ slnx Release 0 警告 0 错误 + 全量测试。
- 扩展仓库：Navigation 20/20 测试 + 全量 1352/1352（消费方模拟：实现 `IMenuContributor` 不标特性 → 菜单项被收集）。

---

## 变更记录

| 日期 | 状态 | 说明 |
|------|------|------|
| 2026-09-15 | 📋 提议 | 初稿——V5 前破坏性变更消除（用户裁定 2026-09-14 列入 V5 前计划）；基于机制勘察（SG1 `FrameworkTypes` 字符串耦合 + `MenuContributorData` 归属 + `ReferencesBackgroundJobs` 先例）；待 Oracle 评审 |
| 2026-09-16 | 📋 提议（修订 1） | **Oracle 评审 PASS WITH CONDITIONS（bg_2c3b5f62）**——5 条件全部吸收：①核查清单 #1 定案**接口化方案**（否决原选项 C 的 internal 访问难题——IVT 脆弱/public 化扩大表面；新增 `IContributorDescriptor` 接口于 `CodeGeneration.Abstractions`，三贡献者数据类型统一实现，基类属性返回接口列表，扩展侧读接口字段无需强转）；②全文命名空间统一方案 B（`TKWF.Ext.Navigation`，修复初稿 4 处 `.Abstractions` 矛盾）；③补依赖矩阵（`MenuItemDefinition → Permissions.Abstractions`，修正"纯 BCL 契约包"表述）；④L90 迁移路径与定案一致；⑤迁移路径补测试侧改造（`FakeMetaContext` override 改接口 + `MainMenuContributor` 删特性）。NICE 项（SG 编译期诊断替代运行时 `Activator.CreateInstance` 异常）记后续 |
