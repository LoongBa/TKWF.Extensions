# ADR-Navigation-贡献者发现机制范围讨论

> **状态**：📋 讨论稿（2026-09-17 用户提出，待框架组裁定）
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

## 变更记录

| 日期 | 状态 | 说明 |
|------|------|------|
| 2026-09-17 | 📋 讨论稿 | 用户提出（#12 不局限于 menu 的问题）——三机制同构证据勘察完成（FrameworkTypes 3 常量 + ProjectMetaContextBase 3 属性）；A/B/C 三方案影响面评估；待框架组裁定 |