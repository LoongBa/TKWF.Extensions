# ADR-Navigation-贡献者机制A+迁移

> **版本**：V0.2.0（目标）
> **状态**：✅ 已裁定（2026-09-17 框架组确认——A+ 完整形态阶段 2 迁 Menu；Oracle bg_ae6d1798 PASS WITH CONDITIONS）
> **关联**：ADR-Navigation-贡献者发现机制范围讨论（§五 A+ 完整形态）、ADR39（D7 被取代）、ADR48（依赖倒置 D7）、ADR61（SG 基类类型判定）、ADR38（扩展包发现）、v4.10.29（A+ 阶段 1 统一基础设施）、v4.10.30（A+ 阶段 2 迁 Menu）
> **关键字**：贡献者、MenuContributor、接口判定、Navigation.Abstractions、新桥消费、契约归位

---

## 目的与目标

将 Navigation 菜单贡献者从旧机制（`[MenuContributor]` 无载荷特性 + 旧桥 `MenuContributors` + 运行时反射）迁移到 A+ 新机制（`IMenuContributor` 接口判定 + 新桥 `Contributors["Menu"]`），并把 Navigation 契约（`IMenuContributor`/`MenuItemDefinition`/`MenuConfigurationContext`）归位到新拆的 `TKWF.Ext.Navigation.Abstractions` 契约包。读者应在 3 句内明白：①删 `[MenuContributor]` 特性——实现 `IMenuContributor` 即自动发现（接口语义，消除漏标静默漏收缺陷）；②契约迁入 Navigation.Abstractions（命名空间保持 `TKWF.Ext.Navigation`，对齐 Account.Abstractions 先例）——Navigation 从"P0 扩展唯一无 Abstractions"收敛；③旧桥 `MenuContributors` 转发到新桥并标 Obsolete，消费方零或极小迁移。

## 问题

**问题现象**：①Navigation 是 P0 扩展中唯一无 `.Abstractions` 项目的模块——契约（`IMenuContributor`/`MenuItemDefinition`/`MenuConfigurationContext`）直接定义在实现项目，契约归属不一致（ADR48 D7 依赖倒置未覆盖），多菜单分区（V0.2.0）后契约无处安放；②`[MenuContributor]` 是无载荷纯标记特性（`AttributeUsage(Class)` 空类）——SG1 仅靠它的类型存在识别贡献者类，漏标静默漏收（无诊断），特性价值趋近于零却制造 `FrameworkTypes` 字符串跨程序集耦合；③A+ 阶段 1（v4.10.29）已建统一 `Contributors` 新桥 + 接口判定，但扩展侧 Initializer 仍读旧桥——新机制数据无人消费，双机制并存冗余。

**触发场景**：主框架演进新增扩展特性常量 = 耦合面扩大；Navigation 契约归属需与 Permissions/Emailing/BlobStoring/Account 对齐；V5 前破坏性变更消除（对齐 #23 ExtractAsync 先例用户裁定）。

**现有方案不足**：母 ADR 草案（Oracle bg_2c3b5f62 评审记录，未落文件）提出"`IContributorDescriptor` 接口 + `MenuContributorData` 迁入 Abstractions"——但 A+ 裁定（Oracle bg_16c84343）用统一 `ContributorDescriptor`（四元组含 TargetKind）替代 `MenuContributorData`，阶段 1 已落地；故本迁移不再需要 MenuContributorData 迁入，聚焦契约拆包 + 新桥消费。

## 使用场景

1. **消费方贡献菜单**（常态）：实现 `IMenuContributor`（同步 `ConfigureMenu`）即被 SG1 自动发现——不再需要 `[MenuContributor]` 标记（接口实现即意图声明，对齐 ADR61 基类类型判定哲学与 `IDomainDataService`/`IDomainEntity` 先例）。
2. **扩展开发**（Navigation 演进）：契约集中于 `TKWF.Ext.Navigation.Abstractions`，实现项目引契约包——后续多菜单分区、自定义 `IMenuDefinitionRepository` 均在此基础上演进。
3. **不适用**：`PermissionContributorAttribute`/`FeatureContributorAttribute` 的移除**不在本 ADR 范围**——A+ 阶段 3 单独迁移（同模式批量），本 ADR 仅收敛 Navigation。

---

## 决策

1. **删 `[MenuContributor]` 特性**：SG1 菜单贡献者发现改**接口判定**（`IMenuContributor` AllInterfaces，对齐 ADR61 基类类型判定）——接口实现即意图声明，编译器强制实现 `ConfigureMenu`，消除漏标静默漏收窗口。
2. **契约归位 Navigation.Abstractions**：新建 `TKWF.Ext.Navigation.Abstractions`，`IMenuContributor`/`MenuItemDefinition`/`MenuConfigurationContext` 迁入，**命名空间保持 `TKWF.Ext.Navigation`**（对齐 Account.Abstractions 先例：`IAccountPasswordManager` 移 Abstractions 但命名空间保持 `TKWF.Ext.Account`）——消费方 `using` 零编译破坏；依赖矩阵：`Navigation.Abstractions → Permissions.Abstractions`（MenuItemDefinition 用 PermissionLogic）+ BCL，**零 CodeGeneration 引用**（3 契约实测）。
3. **新桥消费**：`NavigationExtensionInitializer` 改读 `ProjectMetaContextBase.Instance.Contributors["Menu"]`（`ContributorDescriptor` 统一描述符，阶段 1 已收集接口判定结果）；保留运行时 `Activator.CreateInstance`（阶段 4 编译期 `CreateContributorInstances()` 替代），同步回调语义不变（ADR39 D5）。
4. **旧桥转发 + Obsolete**：主框架 `ProjectMetaContextBase.MenuContributors` 转发到 `Contributors["Menu"]`（`MenuContributorData` 转换）并标 `[Obsolete]`；SG 删 `BuildMenuContributorsMethod` 调用（L290 无条件生成，空数组遮蔽基类转发——删除使基类转发生效）；`MenuContributorData` 类型保留至阶段 4 删（旧桥转发用）。
5. **破坏性**：`[MenuContributor]` 标注者需删特性（机械迁移，实测无外部消费方使用）；命名空间保持零编译破坏；`IMenuContributor` 程序集归属变化（v0.x 可接受）。

---

## 变更记录

| 日期 | 状态 | 说明 |
|------|------|------|
| 2026-09-17 | ✅ 已裁定 | A+ 阶段 2 迁 Menu ADR 落档——接口判定 + 契约归位 + 新桥消费 + 旧桥转发；Oracle bg_ae6d1798 PASS WITH CONDITIONS（4 P1 + 5 P2 吸收：TryGetValue 修正/ADR39 注记/SG override 描述精确/cref 清理/Notifications 引用/Checker 评估/依赖确定/DualCollection 注释/G17B 复核） |
