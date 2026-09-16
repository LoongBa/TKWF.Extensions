# ADR-Navigation-贡献者发现机制范围讨论

> **状态**：✅ 已裁定（2026-09-17 框架组确认，见 §五；本 ADR 作为范围讨论存档，母 ADR 按裁定执行）
> **关联**：ADR-Navigation-MenuContributorAttribute移除与契约归属收敛.md（本讨论的母 ADR）、ADR61（SG 基类类型判定）、D17（扩展机制基座）、ADR47/48（编译期能力聚合）
> **关键字**：贡献者发现、SG1、接口判定、统一收敛、扩展机制基础

---

## 一、讨论背景：为什么"只修 Menu"可能不够

ADR-Navigation-MenuContributorAttribute移除（#12）当前仅处理 Navigation 的 MenuContributor。但勘察揭示**三套贡献者机制完全同构**——机制被复制了三次，每次仅换名字：

| 维度 | Permission 贡献者 | Menu 贡献者 | Feature 贡献者 |
|------|------------------|-------------|----------------|
| **FrameworkTypes 字符串常量** | `"TKWF.Ext.Permissions.PermissionContributorAttribute"` | `"TKWF.Ext.Navigation.MenuContributorAttribute"` | `"TKWF.Ext.FeatureManagement.FeatureContributorAttribute"` |
| **标记特性** | `[PermissionContributor]`（无载荷） | `[MenuContributor]`（无载荷） | `[FeatureContributor]`（无载荷） |
| **ProjectMetaContextBase 属性** | `PermissionContributors`（L69，返回 `PermissionContributorData`） | `MenuContributors`（L78，返回 `MenuContributorData`） | `FeatureContributors`（L88，**复用 `PermissionContributorData`**） |
| **SG1 收集** | `CollectFeatureGates` 查特性 → `GeneratedPermissionContributors` | 同左 → `GeneratedMenuContributors` | 同左 → `GeneratedFeatureContributors` |
| **扩展 Initializer 消费** | `Activator.CreateInstance(contributorType)` → `Define()` | 同左 → `ConfigureMenu()` | 同左 → `Define()` |
| **数据形状** | FullName/Name/ContributorType | 同左 | 同左（复用 Permission 类型） |

三项机制是**同一模式的三个实例**：主框架字符串常量和扩展类名耦合 + 无载荷标记特性 + SG1 编译期收集 + Initializer 运行时 `Activator.CreateInstance` 实例化 + 回调。每个新贡献者类型都要在 `FrameworkTypes` 加一个字符串常量、在 SG 生成器加一段收集逻辑、在 `ProjectMetaContextBase` 加一个属性——**扩展机制基座的耦合面随贡献者种类线性增长**。

---

## 二、核心问题（请框架组裁定）

### 问题 1：范围——是否不局限于 Menu？

本 ADR 当前仅收敛 Menu（`MenuContributorAttribute` 移除 + `MenuContributorData` 迁扩展侧）。但 Permission/Feature 同构问题**原样存在**：

- 若只修 Menu —— `IContributorDescriptor` 接口（本 ADR 为解耦而设计的通用描述符）引入后，Permission/Feature 仍用 `PermissionContributorData`（未实现该接口）——**机制割裂**：同构问题修了 1/3，剩余 2/3 的 FrameworkTypes 字符串耦合继续扩增。
- 若统一收敛 —— 三套机制一次性对齐接口判定（`IxxxContributor` 接口 + `IContributorDescriptor` 统一描述符），FrameworkTypes 三个字符串常量全部消除，`ProjectMetaContextBase` 三个属性统一返回 `IReadOnlyList<IContributorDescriptor>`——**根治同构**，未来新增贡献者类型零 FrameworkTypes 改动。

### 问题 2：归属——贡献者发现是否属于"扩展机制基础"？

TKWF 的扩展机制基座（D17/ADR37-39）定义扩展的"发现 → 启用 → 三钩子接线"。贡献者发现（业务模块向扩展声明权限/菜单/功能定义）是**扩展机制的核心能力**——每个扩展都可能需要"贡献者"模式（导航要菜单贡献者、权限要定义贡献者、功能要特性贡献者、未来 SSO/动态表单等也要各自的贡献者）。

- **主张归属基座**：贡献者发现是扩展机制的**通用基础设施**（SG 编译期收集 + 基类/接口判定 + 元数据描述符），应由 SG 基座统一承载（ADR61 的 "基类类型判定" 已确立同向先例——`InheritsFromDomainDataServiceBase`），非各扩展自理。
- **反方**：当前每扩展自建贡献者机制（特性 + Initializer 消费）已工作；统一是"重构"非"缺陷修复"，影响面大。

### 问题 3：更好设计——框架组是否有统一方案？

本 ADR 的接口化方案（`IContributorDescriptor` + 接口判定）是一种收敛方向，但框架组可能有更优设计：

- **A：三套统一收敛（本 ADR 升级）** —— 三个特性全删，`IxxxContributor` 接口实现即贡献者（对齐 ADR61 基类判定哲学），`IContributorDescriptor` 统一描述符，`ProjectMetaContextBase` 三属性统一返回接口列表（Permission 扩展侧可先实现接口零迁移）。
- **B：通用贡献者声明机制** —— SG 提供**通用** `[ContributorFor(TargetType)]` 或 `IDomainContributor` 基接口 + 注册表，扩展声明"我是 X 的贡献者"，SG 统一收集按目标分类——一机制服务所有扩展（更彻底，但依赖反转，需评估与现有三 Initializer 消费模式的适配）。
- **C：维持现状仅修 Menu（本 ADR 原范围）** —— 最小变更，V5 前快速闭环；Permission/Feature 留待单独评估（但同构问题未根治，FrameworkTypes 耦合继续扩增）。

---

## 三、影响面评估（供裁定参考）

| 方案 | 主框架改动 | 扩展侧改动 | 消费方破坏 | 回归面 | V5 前可行性 |
|------|-----------|-----------|-----------|--------|------------|
| **C（仅 Menu）** | 5 文件（1a-1e） | Navigation 契约归位 | 仅 `[MenuContributor]` 标注者 | 中 | ✅ 高 |
| **A（三套统一）** | FrameworkTypes 3 常量 + SG 收集统一 + ProjectMetaContextBase 3 属性 | Navigation + Permissions + FeatureManagement 契约归位 | 三特性标注者 | 大（三扩展回归） | 🔶 中（需排期） |
| **B（通用机制）** | SG 通用贡献者注册表（较大设计） | 三扩展 + Initializer 消费模式改造 | 大（消费模式变） | 很大 | ⚠️ 低（V5 候选） |

> **折中建议（扩展侧倾向）**：本 ADR 按 C 先行落地（Menu 快速闭环，`IContributorDescriptor` 设计为**通用形状**——非 Menu 专属命名），同时 `PermissionContributorData` 实现 `IContributorDescriptor`（零行为变化，纯加性）——为 A 铺路。框架组裁定 A/B 后，Permission/Feature 的收敛为**纯增量**（特性删 + 接口判定），无需重做本 ADR。

---

## 四、待框架组答复的明确问题

1. **范围**：#12 是否仅收敛 Menu（方案 C），还是三套同构机制统一收敛（方案 A）？
2. **归属**：贡献者发现是否立项为扩展机制基础能力（D17/ADR 层）？
3. **设计**：`IContributorDescriptor` 通用形状 + Permission 先实现（折中建议）是否认可？
4. **时序**：若 A/B，V5 前窗口是否排期（对齐"V5 前消除破坏性变更"裁定）？

---

## 五、框架组裁定（2026-09-17 修订 1——全局最优 A+，Oracle bg_16c84343 咨询）

> 框架组对 §二 四个问题的正式答复。勘察依据：主框架 explore 实证（FrameworkTypes 3 常量 + ProjectMetaContextBase 3 属性 + SG 收集链路 + ADR38/39/67/FeatureManagement D2）+ 扩展侧 explore 实证 + **Oracle 架构咨询（bg_16c84343）**。
> **2026-09-17 修订 1**：用户裁定「V5 前暂无事、无对外宣传，有利于破坏性升级——无需留 V5 后」→ 裁定从 C 升级为 **A+（全局最优，V5 前分步完成）**。

### 裁定结论（修订后）

| # | 问题 | 裁定 |
|---|------|------|
| 1 | **范围** | **A+（三套统一收敛 + 接口判定 + 单桥字典）V5 前完成**——不做 B 的 `ContributorFor` 通用声明（当前 3 种贡献者目标都是接口本身，ContributorFor 是冗余抽象；A+ 落地后 B 为加性演进非破坏性，等真实需求涌现，见 Oracle 升级触发条件） |
| 2 | **归属** | 贡献者发现立项为**扩展机制基础能力**（D17/ADR 层）——V5 前集中实施；实施导向"解耦收缩 + 编译期化"（延续 Permissions v0.8.0"不扩大耦合"立场，且消除 ADR38 L54 遗留反射点） |
| 3 | **设计** | **全局最优形态 = A+**：统一 `ContributorDescriptor`（FullName/Name/ContributorType/TargetKind 四元组）+ SG `AllInterfaces` 接口判定（对齐 ADR61）+ 单桥 `Contributors` 字典（`IReadOnlyDictionary<string, IReadOnlyList<ContributorDescriptor>>`）+ 可选 `CreateContributorInstances()` 编译期实例化（对齐 ADR48，消除 Activator.CreateInstance 遗留反射） |
| 4 | **时序** | **V5 前 4 阶段分步完成**（每步是最终形态子集，无二次破坏）——阶段 1 建基础设施（加性）/ 阶段 2 迁 Menu（原子验证）/ 阶段 3 迁 Permission+Feature（同模式批量）/ 阶段 4 V5 破坏性清理（删旧机制 + 编译期化一次到位）。B（ContributorFor）保持 V5 候选 |

### A+ 最终形态（Oracle 推荐，具体到归属）

| 元素 | 命名 | 归属 |
|------|------|------|
| 统一描述符 | `ContributorDescriptor`（FullName/Name/ContributorType/TargetKind 四元组） | 主框架 `_Domain.SG/CodeGeneration.Abstractions/Metadata/`（替换 Permission/Menu Data 2 类） |
| 贡献者接口 | `IMenuContributor` / `IPermissionContributor` / `IFeatureContributor`（业务方法 Define/ConfigureMenu） | **扩展侧 Abstractions**（Navigation/Permissions/FeatureManagement）——接口定义"贡献者做什么"=扩展业务契约 |
| SG 判定常量 | `FrameworkTypes.IMenuContributorShort` 等 3 短名（对齐 `IUserInfoShortName` 模式） | 主框架 `FrameworkTypes.cs`（替换 `*ContributorAttribute` 3 常量） |
| 单桥属性 | `ProjectMetaContextBase.Contributors`（`IReadOnlyDictionary<string, IReadOnlyList<ContributorDescriptor>>`，key=TargetKind） | 主框架（替换 3 个 virtual 属性；**基类 virtual 不入 IProjectMetaContext 接口**，沿用 L109-110/L135-136 向后兼容模式） |
| 扩展侧消费 | Initializer 读 `Instance.Contributors["Menu"]`（向下转型 `ProjectMetaContextBase`） | 扩展侧（已事实位置） |
| 编译期实例化（可选，建议 V5 一并做） | `CreateContributorInstances()` 桥（对齐 `CreateExtensionInstances()`/`CreateEntityRegistrarInstances()` 先例） | 主框架——消除 ADR38 L54 遗留 `Activator.CreateInstance` 反射点 |

### 4 阶段实施路径（无二次破坏）

| 阶段 | 内容 | 破坏性 | 验收 |
|------|------|:---:|------|
| **1 建基础设施**（主框架，加性） | `ContributorDescriptor` + `Contributors` 字典桥 + SG 通用收集器骨架（接口判定扫 IMenuContributor）；**保留**旧 3 常量/3 桥/2 Data 类（标 `[Obsolete]`） | 无 | 旧扩展照常 + Domain.SG.Tests 全绿 + 新桥空字典 |
| **2 迁 Menu**（原子验证） | Navigation.Abstractions 加 `IMenuContributor`；实现类删 `[MenuContributor]` 改实现接口；SG 扫 `IMenuContributor` → `Contributors["Menu"]`；旧 `MenuContributors` 桥转发到新桥；Initializer 读新桥 | 无（内部转发） | Menu 贡献者照常收集 + ConfigureMenu 调用 + 回归全绿 |
| **3 迁 Permission+Feature**（同模式批量） | 同阶段 2 迁 Permissions/FeatureManagement；三套全走新机制；旧 3 桥全部转发 | 无 | 三套贡献者全绿 + 旧桥转发证明无差异 |
| **4 V5 破坏性清理**（一次到位） | 删 3 `*ContributorAttribute` 常量 + 3 旧桥属性 + 2 Data 类 + SG 旧特性扫描；扩展侧删 3 特性类；**同步实施 `CreateContributorInstances()` 编译期化** | ✅ V5 窗口 | 旧机制归零 + 编译期零反射 + 全量回归 0 警告 0 错误 |

> **为什么无二次破坏**：阶段 1-3 加性扩展（旧机制仍工作，新机制叠加）→ 零破坏；阶段 4 在 V5 破坏性窗口内一次清理到位。每个扩展的迁移是原子的（特性→接口 + Initializer 读新桥同 commit）→ 无"特性已删接口未加"中间破坏态。

### 裁定依据（关键事实）

1. **Oracle 全局最优论证**：A 方案只把"特性"换"接口"，主框架仍需为每种贡献者改 4 处（OCP 未解决）；A+ 用"SG 通用收集器扫任意 I*Contributor 接口 + 按 TargetKind 分桶"使新增贡献者类型只需扩展侧加接口（主框架 SG 配置 +1 行）——真正闭合 OCP。
2. **B 的 ContributorFor 当前冗余**：3 种贡献者目标都是接口本身，`[ContributorFor(typeof(XxxHost))]` 比实现 `IMenuContributor` 多一层无信息量间接；A+ 落地后若真现"一贡献者服务多 host"需求，ContributorFor 是接口判定之上**叠加**特性（加性非破坏）——数据驱动评估，V5 前不押注。
3. **ADR61 背书**：基类链遍历与接口遍历（`AllInterfaces` 含 I*Contributor）同属"基于类型身份判定"——SG 遍历先例已确立。
4. **ADR48 一脉相承**：`CreateExtensionInstances()`/`CreateEntityRegistrarInstances()` 已建立编译期零反射模式，贡献者 `Activator.CreateInstance` 是 ADR38 L54 明示遗留反射点——V5 窗口一并消除，避免 V5 后再改二次破坏。
5. **ADR39 D5 同步裁定维持**：贡献者回调保持同步（ConfigureServices 同步钩子）；异步贡献者与 SG 化哲学冲突（动态定义走 Provider 扩展点，FeatureManagement 先例）——不预留异步。

### 待扩展组执行

1. 母 ADR 状态更新为"框架组已确认，可实施（A+ 范围）"（本裁定同步至母 ADR）。
2. 按 4 阶段路径执行：阶段 1 主框架先（v4.10.29+ 发包 + 部署根同步）→ 阶段 2/3 扩展侧迁移（DLL 模式）。
3. 扩展侧 Abstractions 拆包前置（Permissions/Navigation/FeatureManagement 若缺 `.Abstractions`，对齐 Emailing V0.2.0 拆包先例）。
4. 阶段 4 与 V5 窗口对齐；Permission/Feature 收敛不再单独登记（并入本路径）。

---

## 变更记录

| 日期 | 状态 | 说明 |
|------|------|------|
| 2026-09-17 | 📋 讨论稿 | 用户提出（#12 不局限于 menu 的问题）——三机制同构证据勘察完成（FrameworkTypes 3 常量 + ProjectMetaContextBase 3 属性）；A/B/C 三方案影响面评估；待框架组裁定 |
| 2026-09-17 | ✅ 已裁定（v1） | 框架组确认：C（Menu 闭环）+ 通用形状铺路（IContributorDescriptor + Permission 先实现）；Permission/Feature 收敛登记 V5 后立项 |
| 2026-09-17 | ✅ 已裁定（修订 1） | 用户裁定"V5 前破坏性升级零成本，无需留 V5 后"→ 升级为 **A+ 全局最优**（Oracle bg_16c84343）：统一 ContributorDescriptor + 接口判定 + 单桥字典 + 编译期实例化；V5 前 4 阶段分步完成（每步最终形态子集，无二次破坏）；B（ContributorFor）保持 V5 候选 |