# AGENTS — TKWF.Extensions 开发规则

> 本仓库的**开发规则**。所有 Agent（AI）与人工开发者在本仓库内执行任何开发、文档、版本操作时，必须遵守本文件。
> 本文件由 OpenCode 自动加载（见项目 `opencode.json` 的 instructions 配置）。

---

## 1. 仓库定位

| 项 | 说明 |
|----|------|
| 本仓库 | TKWF 业务扩展包（`TKWF.Ext.*`）——标签、权限、导航、身份、审计等 |
| 主框架 | `../_TKWF/`（TKW.Framework 领域框架）——经 `$(TKWFRoot)` ProjectReference 跨仓库引用 |
| 关系 | 扩展引用主框架**源码**（`Directory.Build.props` 定义 `TKWFRoot`）；**不进入**主框架 slnx |
| 解决方案 | `TKWF.Extensions.slnx`——本仓库扩展的统一构建入口 |

### 公开/私有边界

| 内容 | 仓库 | 说明 |
|------|------|------|
| 扩展代码 + 测试 + 使用指南 | **公开** 本仓库 | `_Framework/`、`_Tests/`、`docs/{扩展名}/` |
| 扩展迭代开发方案 / 审核报告 / ADR / 总览跟踪 | **私有** 主框架 | `_TKWF/docs/03_扩展模块/`（不公开） |
| 扩展机制基座（D17/ADR37-39） | **私有** 主框架 | `_TKWF/docs/` 根与 `_TKWF/docs/02-迭代开发/ADR/` |

---

## 2. 版本体系

- **独立版本**：每个扩展从 **v0.1.0** 起，MinVer 自动管理（`git tag` 前缀 `v`），与主框架版本**完全独立**（各打各的 tag）。
- **文档版本**：文档自身的迭代记录，不与产品版本混用。

---

## 3. 迭代开发流程

1. 编写扩展**使用指南**：`docs/{扩展名}/xxx-使用指南.md`（公开，随 NuGet 发布）
2. 需要独立迭代记录时 → 在**主框架** `_TKWF/docs/03_扩展模块/{扩展名称}/` 编写开发方案
3. 按方案实施（代码 + 测试）
4. 涉及架构决策 → 在**主框架** `_TKWF/docs/03_扩展模块/{扩展名称}/ADR/` 编写 ADR（见 §4 ADR 规则）
5. 审核代码 → 编写/更新**主框架** `_TKWF/docs/03_扩展模块/{扩展名称}/{扩展名称}-审核报告.md`
6. 更新**主框架** `_TKWF/docs/03_扩展模块/总览和跟踪.md`（状态勾选）
7. **推送（push）但不打 tag** → **tag 必须征求同意**（见 §5）

### 提交纪律

- **不频繁提交**：每个逻辑单元（feature/fix/docs）完成后才提交，一次迭代宜收敛为少量提交（通常 1-4 个），避免逐补丁高频提交。
- **提交语义完整**：同一主题的探索性/失败尝试改动应合并为单条有意义的提交，而非保留中间过程。
- **禁止提交调试噪音**：无关的临时修改、未验证的半成品不入提交。

---

## 4. ADR 架构决策记录

**存放位置**（主框架私有）：`_TKWF/docs/03_扩展模块/{扩展名称}/ADR/`

**三问必填**（详见 `docs/AC-Kit/guides/ADR编写指南.md`）：每个 ADR 必须包含三个独立小节，分别回答：
- **目的与目标**——这个决策要达成什么？读者应在 3 句话内明白目标状态。
- **问题**——为了解决什么问题？必须包含问题现象、触发场景、现有方案的不足。
- **使用场景**——用于什么场景？列出具体场景，标注不适用边界。

缺任一小节视为 ADR 不完整，不予批准。

**命名规则**：`ADR-{扩展名称}-{title}.md`（扩展内独立命名，不走主框架 ADR01-39 全局序号），三问必填。

**需要写 ADR 的场景**（不可只记在使用指南里）：
- 外部依赖变更（新增/替换 NuGet 包）
- 扩展间协议变更（接口签名、命名约定）
- 关键设计取舍（如 Tagging 匹配器模型选型）

**不需要写 ADR 的场景**（记在使用指南里即可）：
- 单文件 bug 修复
- 仅影响内部实现的重构
- 测试调整

**生命周期**：ADR 是永久记录，不可删除。即使决策后续被推翻，也应在原 ADR 中标注"已废弃"并引用新 ADR，而非删除原文件。

---

## 5. Tag 纪律

- **任何 `git tag` 操作（创建/推送）必须事先征求用户同意**。tag = 版本发布确认（触发 MinVer 版本号）。
- 日常开发、迭代完成 → 只 `push` 提交，**不自动打 tag**。
- 用户明确同意打 tag 后，使用 `v` 前缀（如 `v0.1.0`），版本号与开发方案目标一致。

---

## 6. 跨仓库引用规则

- **主框架引用**：扩展项目通过 `$(TKWFRoot)`（`Directory.Build.props` 定义 = `../_TKWF/`）引用主框架源码（ProjectReference，编译期依赖）。
- **扩展间引用**：扩展间依赖走 ProjectReference（编译期确定），不走运行时能力发现。
- **NuGet 模式**（后续）：扩展成熟后独立发布 NuGet 包（`TKWF.Ext.{扩展名}`），消费方切 PackageReference。

---

## 7. 指南索引

### 本仓库（公开）

| 文件 | 查阅时机 | 更新时机 |
|------|---------|---------|
| `README.md` | 了解仓库定位、快速开始 | 结构变化时同步更新 |
| `docs/目录结构与版本管理规则.md` | 组织目录、版本管理、发布流程 | 规则变更时 |
| `docs/AGENTS.md` | Agent 路由导航（问题→去读哪篇） | 路由关系变化时 |
| `docs/{扩展名}/xxx-使用指南.md` | 了解某个扩展怎么用 | 扩展功能变更时 |
| `_Framework/{扩展名}/README.md` | 扩展技术规范（随 NuGet 发布） | API 变更时 |

### 主框架（私有，需要时查阅）

| 文件 | 查阅时机 |
|------|---------|
| `../_TKWF/docs/03_扩展模块/总览和跟踪.md` | 查看扩展路线图、各扩展执行状态 |
| `../_TKWF/docs/03_扩展模块/{扩展名称}/` | 查看扩展开发方案、审核报告 |
| `../_TKWF/docs/03_扩展模块/{扩展名称}/ADR/` | 查看扩展架构决策 |
| `../_TKWF/docs/D17-*.md` | 了解扩展机制基座（三钩子、SG1 发现） |

---

## 8. 核心概念速查

### 扩展初始化器

```csharp
[TKWFExtension]
public class TaggingInitializer : ExtensionInitializer<MyUserInfo>
{
    protected override void ConfigureServices(IServiceCollection services) { ... }
    protected override void ConfigureFilters(IServiceCollection services) { ... }
    protected override async Task InitializeAsync(...) { ... }
}
```

- `[TKWFExtension]` 特性 + 继承 `ExtensionInitializer<TUserInfo>` → SG1 编译期发现 → 主框架启动时三钩子自动接线
- 三钩子：`ConfigureServices`（DI 注册）、`ConfigureFilters`（过滤器注册）、`InitializeAsync`（异步初始化）

### 项目模板

```
_Framework/{扩展名}/
├── TKWF.Ext.{扩展名}.csproj    # net10.0，引用 $(TKWFRoot)_Framework\Domain\TKWF.Domain.csproj
├── README.md                    # 技术规范（随 NuGet）
└── *.cs

_Tests/Extension.{扩展名}.Tests/
├── Extension.{扩展名}.Tests.csproj  # xunit.v3，引用本仓库扩展
└── *.cs
```

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-08-30 | v0.1.0 | 初始版本——对齐主仓库 Agents_TKWF.md 规范，适配扩展仓库独立版本/公开私有边界 |