# AGENTS — TKWF.Extensions 开发规则

> 本仓库的**开发规则**。所有 Agent（AI）与人工开发者在本仓库内执行任何开发、文档、版本操作时，必须遵守本文件。
> 本文件由 OpenCode 自动加载（见项目 `opencode.json` 的 instructions 配置）。

---

## 1. 仓库定位

| 项 | 说明 |
|----|------|
| 本仓库 | TKWF 业务扩展包（`TKWF.Ext.*`）——标签、权限、导航、身份、审计等 |
| 主框架 | `../_TKWF/`（TKW.Framework 领域框架）——经 **PackageReference** 引用其 NuGet 包（2026-09-15 迁移，独立于源码构建） |
| 关系 | 扩展引用主框架**发布的 NuGet 包**（`Directory.Packages.props` CPM 集中版本）；**不进入**主框架 slnx |
| 解决方案 | `TKWF.Extensions.slnx`——本仓库扩展的统一构建入口 |

### 公开/私有边界

> **裁定（2026-09-15）**：每个扩展以**最优设计**为目的（借鉴 ABP 但发挥 TKWF 框架优势）；兼容 ABP 仅针对特殊需求（客户提出/指导客户自行实现），**碰巧兼容只需记录**（见 `docs/扩展模块设计思路与ABP兼容策略.md`）。
> 文档归属：**设计文档 + 使用指南 → 公开**本仓库；**内部规划/开发计划 → 私有**主框架。

| 内容 | 仓库 | 说明 |
|------|------|------|
| 扩展代码 + 测试 + 使用指南 + **设计文档**（设计思路/开发方案/ADR） | **公开** 本仓库 | `_Framework/`、`_Tests/`、`docs/{扩展名}/`、`docs/扩展模块设计思路与ABP兼容策略.md` |
| 内部规划（总览跟踪 / 开发计划 / 审核报告 / 对标清单规划表） | **私有** 主框架 | `_TKWF/docs/03_扩展模块/`（不公开） |
| 扩展机制基座（D17/ADR37-39） | **私有** 主框架 | `_TKWF/docs/` 根与 `_TKWF/docs/02-迭代开发/ADR/` |

> **维护/提交归属裁定（2026-09-16）**：**主框架中扩展模块相关内容**（`_TKWF/docs/03_扩展模块/` 全目录 + 主框架文档中扩展模块状态段落）**由本仓库负责维护与提交**——因本仓库是 public（扩展模块内容对外可见、随扩展演进），主框架仅作物理承载。即：扩展模块相关文档的增改由本仓库侧执行并直接提交到主框架 git（含 push，不打 tag），不依赖框架组代劳；框架机制基座（D17/ADR37-39 等）仍归主框架维护。

---

## 2. 版本体系

- **独立版本**：每个扩展从 **v0.1.0** 起，MinVer 自动管理；各扩展 **csproj 设 `<MinVerTagPrefix>{扩展名}/v`** 只匹配自己的前缀 tag（如 `Permissions/v0.9.0` → 版本 `0.9.x`），**扩展间版本完全独立、互不影响**；契约包（`*.Abstractions`）独立 tag 独立演进；**公共内容（构建基建/无扩展归属）用无前缀 `v` tag**（既有 `v0.1.0`~`v0.8.2`）。与主框架版本**完全独立**（各打各的 tag）。
- **文档版本**：文档自身的迭代记录，不与产品版本混用。

---

## 3. 迭代开发流程

1. 编写扩展**使用指南**：`docs/{扩展名}/xxx-使用指南.md`（公开，随 NuGet 发布）
2. 编写扩展**设计文档/开发方案**：`docs/{扩展名}/xxx-开发方案.md`（**公开**本仓库，2026-09-15 裁定后新方案落公开仓库；历史方案在私有主框架，渐进迁移）
3. **方案评审** → 由 **Oracle** 评审开发方案（Momus **仅评审** `.sisyphus/plans/` 目录下的工作计划，不评审 TKWF 开发方案）
4. 评审通过后按方案实施（代码 + 测试）
5. 涉及架构决策 → 编写 **ADR**：`docs/{扩展名}/ADR/ADR-{扩展名}-{title}.md`（公开本仓库；历史 ADR 在私有主框架，渐进迁移）
6. 审核代码 → 编写/更新**主框架私有** `_TKWF/docs/03_扩展模块/{扩展名称}/{扩展名称}-审核报告.md`（审核报告属内部）
7. 更新**主框架** `_TKWF/docs/03_扩展模块/总览和跟踪.md`（状态勾选）
8. **推送（push）但不打 tag** → **tag 必须征求同意**（见 §5）

> **主框架扩展模块文档提交（2026-09-16 裁定）**：步骤 6/7 产生的**主框架 `_TKWF/docs/03_扩展模块/` 文档增改由本仓库侧直接提交到主框架 git（含 push，不打 tag）**——不依赖框架组代劳（见 §1 维护/提交归属裁定）。

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

- **主框架引用（2026-09-15 迁移为 PackageReference）**：扩展项目通过 **PackageReference** 引用主框架 NuGet 包（`TKWF.Domain` / `TKWF.Utility` / `TKWF.Core` / `TKWF.Domain.FreeSql` / `TKWF.BackgroundJobs(.Quartz)` / `TKWF.Utility.DataPort(.Providers.MiniExcel)` / `TKWF.CodeGeneration.Abstractions` 等，版本集中管理于 `Directory.Packages.props` CPM）——**独立于主框架源码构建/发布**（消费方亦按 NuGet 模式使用，提前暴露兼容问题；无需检出 `../_TKWF`）。
- **SG1 分析器**：实体扩展经 `<PackageReference Include="TKWF.CodeGeneration" PrivateAssets="all" />` 引入——该包为**纯 Analyzer 包**（`analyzers/dotnet/cs/`，Roslyn 自动加载），**不要**用 `<Analyzer>` 元素 / `OutputItemType="Analyzer"`（Roslyn 增量缓存 bug）/ `build\refs` 预编译 DLL（废弃路径）。
- **扩展间引用**：扩展间依赖走 ProjectReference（编译期确定，打包时自动转版本依赖），不走运行时能力发现。
- **双模式并存**：本地开发/扩展仓库构建 = PackageReference（主框架包）；若需调试主框架源码新 API，可临时切 ProjectReference 或等主框架发新包——**消费方视角与 NuGet 模式一致**。
- **双模式引用开关（2026-09-16 方案 B 定案 DLL 模式，用户裁定）**：`UseLocalFw`（`Directory.Build.props` 集中定义，默认 `true`）——`true` = **DLL 模式**（本地联调：DLL 引用部署根 `$(TKWFDeployPath)\build\refs\`（对齐消费方 DMP-Lite，**零跨仓库源码耦合**）；`false` = **NuGet 发布**（PackageReference，CI/发布用，`ci-publish.yml` env `UseLocalFw: 'false'` 验证真实消费方视角）。实现：**不导入主框架 `TKWF.Domain.targets`**（其会无条件注入 build/refs 的 Analyzer，与扩展纯 Analyzer NuGet 包冲突——Roslyn 增量缓存 bug）；Directory.Build.props 自建集中 DLL 引用块（Core/Utility/Abstractions/Domain + 运行时传递依赖）+ 各扩展 csproj 按需 `Reference`（CodeGeneration.Abstractions/Domain.FreeSql/特殊包）。扩展 csproj 引用块按 `Condition="'$(UseLocalFw)' == 'true'"` 条件化；命令行 `-p:UseLocalFw=false` 覆盖。**CI Analytics 门控**：4.10.27（含 Utility.Analytics）发布前 `ANALYTICS_ENABLED=false` 排除 Analytics（nuget 模式 restore 失败），CI 用显式项目列表（slnx 不支持按属性排除项目）。**DLL 模式维护**：主框架源码构建后 `F:\TKWF_FRAMEWORK_PATH\build\refs\`（TKWFDeployPath）自动同步（源码树 refs 与部署根经 `_PushToRefs` 双写）——升级主框架包需重同步部署根。**Analytics 本地走 DLL 即可**（部署根 Utility 4.10.28 含 Utility.Analytics）。**⚠️ V4.10.32（A+ 阶段 4）SG Analyzer 版本锁**：SG1 分析器（`TKWF.CodeGeneration`）**恒用 NuGet 纯 Analyzer 包**（DLL 模式**不**从部署根 refs 挂 `<Analyzer>`——refs DLL 缺 Roslyn 依赖，且 AGENTS §6 裁定废弃该路径）。**破坏性后果**：主框架破坏性版本（如 v4.10.32 删旧桥/Data 类）发布后，扩展 DLL 模式（运行时用部署根 4.10.32 DLL + Analyzer 用 NuGet 旧包）会**编译失败**（旧 SG 生成物引用已删类型 / 新 Initializer 调 `CreateContributorInstances` 在旧 Abstractions 缺失）——**必须等主框架新包发布 + CPM 升级（`Directory.Packages.props`）后验证**。扩展侧 A+ 阶段 4 代码（3 Initializer 编译期化 + 39 .g.cs 死 using + xCodeGen 模板）已验证就绪，待 4.10.32 CPM 升级后全量验证。—— **✅ 状态闭环（2026-09-22）**：4.10.32 CPM 升级已全量验证（8ac0ff9，slnx 构建 0 错误 + 全量测试全绿）；**v4.10.33（i18n 国际化 Phase 1，零破坏性影响实证）CPM 升级亦已全量复验**——12 包 4.10.32→4.10.33（Directory.Packages.props），slnx 构建 0 错误 + 29 测试项目全绿（1385 用例），扩展侧零代码改动（新增 `TKWF.Framework.Localization` 主框架内部设施，扩展无需引用）；**v4.10.34（i18n Phase 2——构建时发射器 + JsonFileLocalizationContributor + 漂移守卫，零强制迁移实证）CPM 升级复验**——11 包 4.10.33→4.10.34，slnx 构建 0 错误 + 29 测试项目全绿（1394 用例），扩展侧零代码改动（注：CPM TKWF.* 实为 11 包非 12，此前注记笔误）；**v4.10.37（ADR82 Core 解耦 Localization 编译期强依赖——EnumDisplayName 键化契约 + G11–G14 收尾，零强制迁移零代码调整）CPM 升级闭环**——11 包 4.10.34→4.10.37（Directory.Packages.props），slnx 构建 0 错误 + 29 测试项目全绿（1394 用例），扩展侧零代码改动（框架组逐项核查实证：扩展仅 3 处消费 IFrameworkLocalizer 接口契约未变、无 FrameworkResources/[Display(ResourceType)] 使用、G11–G14 零直接消费；NuGet 依赖拓扑 Localization 承载 Core→Domain/Utility 对引 TKWF.Domain 的扩展传递语义无变化；详见主框架 `docs/03_扩展模块/v4.10.37-扩展模块组通知与自查说明.md`）。⚠️ 已知测试竞态备忘：`FileManagerTests.ConcurrentUpload_SameFolderSameName_SameContent_IdempotentNoException` 单独 filter 跑稳定失败（双并发预查 null 撞 UX 约束转业务异常）、全量并行跑通过——V0.2.0 既有竞态（依赖"后完成者读到先行者已提交"时序），非 v4.10.37 回归（v4.10.37 无数据访问/事务行为变更），留待 FileManagement v0.3.0 加固。—— **✅ 已闭环（2026-10-01）**：FileManagement v0.3.0 竞态加固——主表 `UX_ManagedFile_Folder_Name` 约束败者重查按内容决策（同 SHA256 → 幂等返回既有 / 不同内容 → 复用已写 Blob 追加新版本），消除"该目录下已存在同名文件"误报；并发测试稳定通过（同内容 + 新增强制双成功用例，文件模式 SQLite 规避 `:memory:` 单连接池争用）；版本号并发冲突语义不变。**v4.10.39（分组聚合 API——FreeSqlQueryableExtensions GroupByAsync/GroupCountAsync×2/GroupSumAsync，Oracle 评审 PASS + 13/13 SQL 下推断言测试）CPM 升级与扩展侧适配闭环（2026-10-01）**——11 包 4.10.37→4.10.39（Directory.Packages.props；⚠️ `TKWF.CodeGeneration` 恒 NuGet 纯 Analyzer 包，框架 4.10.39 未打 tag 未发布 NuGet（nuget.org 最新 4.10.38）→ **暂锁 4.10.38 桥接**（4.10.39 仅运行时 FreeSql 改动不动 CodeGeneration.Abstractions，兼容；框架发布后同步提升））；**✅ 发布闭环（2026-10-01）**：框架 tag v4.10.39 触发 CI 全链路发布（32 个 TKWF.* 包 + TKWF.XCodeGen tool 推 nuget.org，含 TKWF.CodeGeneration 4.10.39 纯 Analyzer 包）——CPM **全量 lockstep 4.10.39**（Directory.Packages.props 解除 4.10.38 桥接），slnx 构建 + 全量测试复验；**扩展侧试点替换完成**（对齐框架审核报告 §六）：AuditLogging V0.4.2 `CountByServiceAsync`/`CountByUserAsync`、SecurityLog V0.3.1 `GetTopFailedByUserAsync`/`GetTopFailedByIpAsync` → `GroupCountAsync(key, topN)` SQL 下推（GROUP BY+COUNT+ORDER BY COUNT DESC+LIMIT，替代 `Take(100_000)`+内存 GroupBy；空白键过滤 `!string.IsNullOrWhiteSpace` 下推 SQL）；Tagging V0.4.3 `GetFrequencyAsync`（复合键 TopN）+ `GetDimensionDistributionAsync`（`GroupByAsync` 原语 count 降序）SQL 下推，**`GetTrendAsync` 保留内存分桶**（V0.4.3 决策：Week 周一零时无 SQL 可翻译表达式 + 键序与 TimeBucket 序语义不一致，命中窗口通常数千行）；**测试项目双模式条件化根因修复**（26 个测试 csproj：TKWF.* 包条件化 + Domain.FreeSql/CodeGeneration.Abstractions DLL 引用 + 补 FreeSql.DbContext/FreeSql.Extensions.Linq——DLL 模式下 Domain.FreeSql DLL 引用不传递其 NuGet 依赖，此前依赖测试项目无条件的 Domain.FreeSql nuget restore 传递，4.10.39 未发布即暴露）；slnx 构建 0 错误 + **31 测试项目 1499 用例全绿**（新增 3 用例：三扩展 SQL 下推断言）；使用指南补依赖条件说明（FreeSql 管线前置 + ORM 边界 ADR15 + 趋势保留内存决策 + HitTime UTC 写入约定）。
- **SG Analyzer 本地测试机制（2026-10-01 方案 B 用户裁定）**：SG1 分析器（`TKWF.CodeGeneration`）恒用 NuGet 纯 Analyzer 包（版本锁），为支持 **NuGet 发布前本地测试分析器变更**（消除发布周期等待），采用**本地 NuGet feed**：框架组全量部署脚本（`_PushToRefs` 双写 refs 处）并行执行 `dotnet pack _Domain.SG\CodeGeneration\TKWF.CodeGeneration.csproj -p:Version={当前迭代版本}-local -o $(TKWFDeployPath)\local-feed\`（仅此 1 包——运行时已由 refs DLL 覆盖；`-local` 后缀强制防与 nuget.org 正式版冲突）；扩展侧**本地源不入库不进 CI**——开发者用户级 NuGet.config（%AppData%\NuGet\NuGet.Config）添加 `$(TKWFDeployPath)\local-feed`，测试未发布分析器时 CPM 引 `{版本}-local`，框架发布正式版后切正式号 + 删本地源；机制零变更（本地 nupkg 与 nuget.org 发布物完全一致——analyzers/dotnet/cs 布局 + Abstractions 并列 + SuppressDependenciesWhenPacking）。已转告框架组（2026-10-01）。

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

- `[TKWFExtension]` 特性 + 继承 `ExtensionInitializer<TUserInfo>` → SG1 编译期发现（生成能力清单）；**是否启用由消费方在领域初始化器上 `[TKWFEnabledExtension(typeof(XxxExtensionInitializer<>))]` 白名单声明决定**（发现不自动启用，IsEnabled 默认 false）。消费方声明后三钩子自动接线
- 三钩子：`ConfigureServices`（DI 注册）、`ConfigureFilters`（过滤器注册）、`InitializeAsync`（异步初始化）

### 项目模板

```
_Framework/{扩展名}/
├── TKWF.Ext.{扩展名}.csproj    # net10.0，PackageReference 引用 TKWF.Domain（CPM 集中版本）
├── README.md                    # 技术规范（随 NuGet）
└── *.cs

_Tests/Extension.{扩展名}.Tests/
├── Extension.{扩展名}.Tests.csproj  # xunit.v3，引用本仓库扩展
└── *.cs
```

### 扩展使用 SG1/xCodeGen（框架原生开发方式）

> **✅ V0.3.0+ 正确路线（v4.10.53 ADR90 领域自治根治，主框架 `tkwf-extension` skill 磨合版定稿——`../_TKWF/docs/AC-Kit/skills/tkwf-extension/SKILL.md` §4.7 六条心得必读）**：扩展模块 = 业务领域开发方式（Entity → SG1 → xCodeGen → DataService → Service 门面继承 `DomainServiceBase` → 消费方 `User.Use<T>()`）。**本仓库 40+ Store/Service/Provider/Manager 类须按此范式分批整改**——代表性实证：Settings V0.3.0（删 `ISettingStore/SettingStore`；`SettingManager : DomainServiceBase` + `AddConstructibleService`）、Permissions V0.9.1（删 `EntityDACPermissionStore`；`PermissionChecker : DomainServiceBase<TUserInfo>`）、Tagging V0.4.4（三 Store 继承基类 + `AddConstructibleService`）。铁律：**IDomainUser 永不注册 DI（经基类 `User`）；领域服务必须继承 `DomainServiceBase`；禁 Store 伪 DataService 层；DataService 禁构造注入（DI004 默认 Error）；`InitializeAsync` 启动加载经 System 作用域**。

> **⛔ 红线：不手写 Store（2026-10-05 显式强化——AuthCenter 归层阶段 1 曾犯，已回退）**：**禁止创建任何 `I{Xxx}Store`/`{Xxx}Store`（DataService 薄包装）**——SG1/xCodeGen 生成的 DataService 已提供全部 CRUD 原子方法（`EntityGetAsync`/`EntitySelectAsync`/`EntityCreateAsync`/`EntityUpdateAsync`/`EntityUpdateWhereAsync`/`EntityCreateBatchAsync`/`EntityDeleteBatchAsync` 等），**领域服务门面直接经 `User.Use<XxxDataService>()` 懒加载组合即可，不需要也不允许再包一层 Store**（CRUD 与 DataService 完全重叠 = 伪层；settings `ISettingStore`/Permissions `EntityDACPermissionStore` 曾为反例）。
> - **判据**：门面需要数据访问时 → 直接 `User.Use<具体 DataService>()`（如 `User.Use<SsoClientEntityDataService>()`）；**只有当契约需跨扩展消费（数据属主在他扩展主包）时才经 Abstractions 契约包定义接口**——但该接口是**对外的领域服务门面契约**（含业务语义），不是 DataService 的 CRUD 薄包装；零手写 CRUD（SG1 已生成）。
> - **自检**：新增文件若以 `Store` 结尾或方法仅是 `EntityXxxAsync` 的透传 → 删除，改门面直接组合 DataService。

### 扩展开发强制 Skill 路由（2026-10-05 新增，违反即失败）

> **扩展仓库也是 TKWF 消费方**（用户裁定 2026-10-05）：扩展用主框架 API 开发（消费框架），产物被装配进消费项目——因此**与消费方项目同款强制 Skill 路由**（参考 `DMP-Lite/Agents_Use_TKWF.md §7`，路由方向面向扩展开发）。任何扩展开发任务必须按表格加载对应 Skill，禁止使用通用 Agent 分类或手动编写绕过。

| 任务 | 必须使用的 Skill | 关键参数 | 禁止行为 |
|------|-----------------|---------|---------|
| 扩展整体开发/重构/整改（门面/Store/Service/Initializer/测试宿主） | **`tkwf-extension`** | `{Ext}=扩展名` | 禁手写 Store/构造注入/裸 ORM（红线）；禁通用 category 绕过 |
| 扩展实体编写（Entity） | `tkwf-entity` | `{Ext}=扩展名` `{Entity}=实体名` | 禁手写 `IDomainEntity`/`[Key]`/`IsFromPersistentSource`（SG1 自动生成） |
| 扩展 Service/业务规则 | `tkwf-service` | `{Ext}=扩展名` | 禁手动写 Service、禁绕过门面范式 |
| 扩展测试（生产路径 + 契约测试） | `tkwf-test` | `{Ext}=扩展名` | 禁手写测试宿主绕过生产路径（StubDomainUser 掩盖守卫缺陷先例） |
| 接入/启用/使用他扩展（引用→白名单→Web 装配→`User.Use` 门面） | `tkwf-use-extension` | `{Ext}=被消费扩展名` | 禁绕过白名单/门面 AOP 路径 |

> **前置检查**：接到扩展开发任务时，第一步先加载并阅读对应 Skill 全文，按其规范执行，不得凭记忆或通用做法替代（对齐消费方 `Agents_Use_TKWF.md §7` 前置检查）。
> **Skill 位置**：主框架 `../_TKWF/docs/AC-Kit/skills/{skill名}/SKILL.md`（本仓库通过软链/路径引用，无需手动加载——OpenCode 按 AGENTS.md 路由自动装载）。

> **实践思路**（V0.2.0 起）：扩展模块**可以**采用框架原生开发方式——引入 SG1 分析器 + xCodeGen 生成 DTO/DataService/Conditions/IDomainEntity 实现，与业务领域项目使用同一套开发模式（tkwf-entity / tkwf-service skill）。这使扩展获得自动建表、REST/GraphQL API 暴露、Dto 自动裁剪等框架能力，减少手写样板代码。
>
> **数据访问红线（2026-09-07 用户裁定）**——扩展数据访问层必须遵循以下三条，违反即架构违规：
> 1. **不允许直接使用 ORM（依赖）**——扩展代码不得注入 `IFreeSql`（或其他 ORM）直接写查询/SQL，除非业务有特殊需要**且**框架不可能满足（两条同时成立，否则不可豁免；历史上 DataPort raw SQL 的 UTC DateTime 语义曾为此类，ADR89 `IEntityDAC<T>.UpdateWhereAsync` 落地后已收编，2026-10-03 起零逃生口）；裸 ORM 无委托解析、autocommit、不参与 UoW，破坏事务一致性。发现能力缺口时**先转达框架组**补条件原子/聚合原语（先例：ADR89），而非直接注入 IFreeSql。
> 2. **不允许直接使用 `IEntityDAC<T>`**——扩展不得在自定义 Store/Service 中直接注入 `IEntityDAC<T>` 手写数据访问；须用 SG1/xCodeGen 生成的 DataService（`DomainDataServiceBase`/`DomainReadOnlyDataServiceBase` 派生，`[GenerateController(FromDataService=true)]` 暴露 API）。极少情况需征求用户同意并记录，且不能作为示范（Permissions `EntityDACPermissionStore` 曾为反例，2026-09-07 已委托 `PermissionGrantEntityDataService` 整改；2026-10-03 A 批（ADR88）构造注入改懒加载后彻底合规）。
> 3. **扩展应发挥组装优势**——TKWF 扩展模块机制的价值在于业务领域可灵活组装基础扩展、甚至用扩展机制拆分业务子领域；扩展自身数据访问必须走框架原生路径（SG1 生成 DataService），否则丢弃框架优势、本末倒置。
>
> **标准数据访问路径**：`DataService（DomainDataServiceBase/DomainReadOnlyDataServiceBase）→ IEntityDAC<T>（ORM 无关契约）→ FreeSqlEntityDAC/EFCoreEntityDAC（实现层，UoW 事务绑定）`。扩展只依赖 DataService，不触碰 IEntityDAC/ORM。
>
> **跨表查询（VEntity，2026-09-07 实施）**：扩展需要跨表 JOIN 时——**多对一**（N 行映射 → 1 行主表，JOIN 后行数不变、主键透传唯一稳定）用 **VEntity**（SQL View 实体）：`[Table(Name="vw_...", DisableSyncStructure=true)]` + `[DomainGenerateCode(IsView=true, ViewSql=<PG>, ViewSqlSQLite=<SQLite>, ExposeGraphqlQuery=true)]` + `partial class`（不手写 `IDomainViewEntity`/`IsFromPersistentSource`）。xCodeGen **跳过 VEntity 的 DataService/Conditions 模板**（Engine.cs L42-46）→ 手写只读 DataService（`DomainReadOnlyDataServiceBase<TEntity,TDto>` 2 参数版 + 注入 `IEntityReadOnlyDAC<T>`，绝不用 `IEntityDAC<T>`）+ Initializer `TryAddScoped` 手动注册（VEntity 不自动注册）；不标 `[GenerateController(FromDataService=true)]`（Store 内部能力经门面暴露，GraphQL 经 `ExposeGraphqlQuery` 自动）。**接口不变策略**：视图行映射回原实体，对外接口签名不变。**生产部署**：SyncViewsAsync 只跑开发环境建视图，**生产需 DBA 手动执行 ViewSql**（写入使用指南）。**一对多主从聚合**（DataDictionary/PrintTemplates 带缓存）**不用 VEntity**（行膨胀 + Id 造假键 + 缓存优势），保持两步查询。先例：Identity `vw_UserRoleView` + Notifications `vw_UserNotificationView`（见主框架 `docs/03_扩展模块/VEntity跨表查询升级-开发方案.md`）。
>
> **数据访问红线整改清单**（2026-09-07 盘点 → **已全部完成，2026-09-07 当日闭环**，2026-09-10 复核确认）：
> - ✅ 规则 1 违规（裸 IFreeSql）10 扩展：Settings/Emailing/BlobStoring/Account/DataDictionary/PrintTemplates/Identity/AuditLogging/DataPort/Notifications——全部委托 DataService（374 测试全绿）
> - ✅ 规则 2 违规（直接 IEntityDAC）：Permissions `EntityDACPermissionStore` → 委托 `PermissionGrantEntityDataService`（47/47 全绿）
> - ✅ 移除 10 个 Initializer 手动 DataService 注册（SG 自动注册覆盖生产）；测试 Host 补模拟注册
> - 见主框架 `docs/03_扩展模块/ADR-扩展数据访问红线.md`（整改清单 + 决策完整记录）
>
> **"无必要勿增SG"澄清**（2026-09-06 用户裁定）：扩展实体用 `[DomainGenerateCode]`（SG1 原生方式）**不计入"增 SG"**——消费方标准 TKWF 项目 SG1 已接线，扩展实体被自动扫描生成（IDomainEntity/DTO/DataService），**零额外配置**（先例：PrintTemplates/AuditLogging/DataDictionary 全部如此）。"无必要勿增SG"特指**扩展引入独立生成管线/分析器依赖**（如 Permissions.Validation 的 Analyzer，消费方需额外接线）——此类才需权衡"引入 SG 增加消费方配置复杂度/对接管线成本"。**判据：扩展实体是否需要 DTO/DataService/API 生成（有持久化 + 查询/管理需求的实体默认用 SG1）；纯内存/纯运行时扩展（Tagging/Metrics/Dashboard/DataPort 核心）无需 SG1。**
>
> **与"预编译库"模式的关系**：两种模式并存——简单横切扩展（如 Tagging，纯内存服务）保持预编译库；有持久化 + 管理 API 的扩展（如 Permissions V0.2.0）可升级为 SG1 原生。
>
> **扩展启用（V4.9.85）**：扩展 DLL 被消费方引用后，SG1 经 `ReferencedAssemblySymbols` 发现 `[TKWFExtension]` 初始化器（生成能力清单）；但**发现 ≠ 启用**——扩展的 `IsEnabled` 默认 false，三钩子默认不执行。消费方须在自身 `DomainHostInitializerBase<T>` 派生类上标注 `[TKWFEnabledExtension(typeof(XxxExtensionInitializer<>))]` 白名单声明（AllowMultiple），扩展才真正启用、三钩子才执行。未声明 → 发现但默认不启用。

**csproj 接线要点**（2026-09-15 迁移为 PackageReference 独立构建——主框架包版本集中 `Directory.Packages.props` CPM）：

```xml
<!-- ① 框架抽象引用（[DomainGenerateCode] 属性所在程序集） -->
<PackageReference Include="TKWF.CodeGeneration.Abstractions" />

<!-- ② SG1 分析器：纯 Analyzer 包（analyzers/dotnet/cs/，Roslyn 自动加载）——
     不用 <Analyzer> 元素（Roslyn 增量缓存 bug）/ 不用 OutputItemType="Analyzer" /
     不用 build\refs 预编译 DLL（废弃路径，2026-09-15 迁移前方式）；
     PrivateAssets="all" 防止作为运行时依赖透传给消费方 -->
<PackageReference Include="TKWF.CodeGeneration" PrivateAssets="all" />

<!-- ③ 主框架运行时引用（按需） -->
<PackageReference Include="TKWF.Domain" />
<PackageReference Include="TKWF.Domain.FreeSql" />
</ItemGroup>

<!-- ④ SG1 生成文件排除物理编译（避免重复 Decorator 冲突） -->
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)GeneratedFiles</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
<ItemGroup>
  <None Remove="$(CompilerGeneratedFilesOutputPath)\**" />
</ItemGroup>

<!-- ⑤ xCodeGen（可选，仅 Debug 触发）：定义配置路径走 D1 集中化 -->
<PropertyGroup>
  <_XCG_ConfigPath>$(MSBuildProjectDirectory)\.xCodeGen\{扩展名}.xCodeGen.json</_XCG_ConfigPath>
</PropertyGroup>
```

**实体编写要点**（按 `tkwf-entity` skill）：
- 实体标注 `[DomainGenerateCode(UserType = nameof({扩展名}UserInfo), DefaultPageSize = 50)]`，`partial class`
- **不手写** `IDomainEntity`/`IsFromPersistentSource`（SG1 自动生成）
- 用 FreeSql `[Column]` 特性（`IsPrimary`/`IsIdentity`/`Position`），不用 BCL `[Key]`/`[DatabaseGenerated]`
- 扩展自建 `{扩展名}UserInfo : SimpleUserInfo` 作为通用用户类型（扩展不知道消费方 UserInfo 类型）

**注意**：
- SG1 自诊断门禁（V4.9.15+）编译语义判断，不依赖 `TKWFRole`——扩展无需设 TKWFRole 即可接入
- 主框架发布新版本后，升级 `Directory.Packages.props` 中 `TKWF.*` 版本并重新构建验证（PackageReference 模式）
- 扩展作为 DLL 被消费方引用时，SG1 经 `ReferencedAssemblySymbols` 发现扩展内 `[TKWFExtension]` 初始化器——与业务领域 SG1 生成不冲突，二者并存。发现 ≠ 启用：消费方须 `[TKWFEnabledExtension]` 白名单声明后三钩子才执行（V4.9.85 ADR47）

### xCodeGen 生成物管理（.g.cs 入库政策，2026-09-14 裁定）

**标准管线**（实体型扩展固有）：实体 `[DomainGenerateCode]` → SG1 分析器产生元数据（obj/GeneratedFiles Meta/ProjectMetaContext）→ **xCodeGen CLI 生成 `.g.cs`**（`DataService` 基座含 internal 原子转发 `EntityCreateBatchAsync`/`EntitySelectAsync`/`EntityDeleteBatchAsync`、`Entity`/`Dto`/`Conditions`）→ 手写分部 `.cs` 只编写**业务方法**（public 委托包装，对齐 `AuditLogEntityDataService.cs` + `.g.cs` 分部对）。

**`*.g.cs` 入库（不忽略）**——本仓库是**源码库**（消费方 ProjectReference/源码审查），生成物是提交的组成部分：
- fresh clone / CI 必须无前置步骤可构建；提交必须可复现；PR 可见生成物变更。
- `.gitignore` 已移除全局 `*.g.cs` 规则（2026-09-14）；obj/GeneratedFiles 分析器中间产物仍由 `obj/` 规则忽略。
- **重生成纪律**：实体/模板变更后运行 `pwsh .xCodeGen/run-xcodegen.ps1`（单扩展 `-Ext {扩展名}`），将更新的 `.g.cs` 一并提交。**禁止**只改实体不重新生成/提交生成物（= 源码与生成物漂移，曾致 Permissions 生成物 2 周陈旧）。
- 配置集中：`.xCodeGen\extensions\{扩展名}.xCodeGen.json`（`TargetProject` 指向项目；`OutputDir` 用**相对 OutputRoot 的 `..\Entities`/`..\DataServices`** 风格——2026-09-14 修复 permissions 配置曾用绝对式 `..\..\_Framework\...` 导致生成物落入 `_Framework\_Framework\` 重复目录 + 真实文件 2 周未刷新）。
- 测试项目模拟消费方时同样走此管线（先例：`metrics-tests.xCodeGen.json`——测试宿主内实体 → xCodeGen 生成 → 手写分部业务方法）；宿主元数据上下文用 **SG1 生成的 `ProjectMetaContext`**（消费方真实形态，含 ADR61 DataService 自动注册），不手写空桩。
- 本仓库 MSBuild 未导入 `TKWF.Domain.targets`（`_XCG_Run` 构建期自动生成目标不生效）——xCodeGen 由 `run-xcodegen.ps1` 手动驱动（对齐 DMP-Lite 模式的前提是构建期自动生成，未接入则必须入库，二者择一；本仓库取入库）。

### 扩展模块生成物健康自检清单（2026-09-14 审计固化）

实体型扩展迭代收尾时逐项核对（防止重犯 Permissions/Approval 类漂移缺陷）：

| # | 检查项 | 判定 | 失败后果 |
|---|--------|------|---------|
| 1 | 实体型扩展 ↔ `.xCodeGen/extensions/{扩展名}.xCodeGen.json` **1:1 存在** | 每个含 `[DomainGenerateCode]` 实体的扩展必须有配置 | 无配置 → run-xcodegen.ps1 无法重生成 → 生成物陈旧（Approval 4-5 天陈旧先例） |
| 2 | 配置 `OutputDir` 用**相对 OutputRoot 的相对路径**（`..\Entities`/`..\DataServices`），`TargetProject` 存在 | 相对风格 + TargetProject 可解析 | 绝对式路径相对 OutputRoot 解析翻出目录 → `_Framework\_Framework\` 重复目录 + 真实文件不刷新（Permissions 先例） |
| 3 | 实体变更后已重跑 `run-xcodegen.ps1` 并提交 `.g.cs` | git diff 含本次实体对应的 `.g.cs` 变更 | 源码与生成物漂移；提交无法复现 |
| 4 | 生成物时间戳新鲜（提交前核对 `Get-Item *.g.cs | select LastWriteTime`） | 与实体最后变更同日 | 陈旧生成物静默编译通过但内容过时（Permissions/Approval 先例） |
| 5 | 每个 DataService 为"生成 `.g.cs` 基座 + 手写分部业务方法"分部对 | 手写 `.cs` 不含完整 CRUD/构造器（那些在 `.g.cs`） | 手写基座绕过生成管线，消费方路径不忠实（Metrics 测试早期先例） |
| 6 | 测试宿主 `OnRegisterInfrastructureServices` 返回 **SG1 生成的 `ProjectMetaContext`** | 不手写 `IProjectMetaContext` 空桩（`TestMetaContext` 类反例） | 空桩缺 `GetOrCreateInstance`（xCodeGen 读取失败）；缺 ADR61 自动注册验证 |
| 7 | 生成骨架 `.biz.cs` 编译通过 | `.biz.cs` 含 `using System.Collections.Generic;`（EntityEmpty 模板缺陷 2026-09-14 已修源码+部署副本） | 模板回归致 `List<ValidationResult>` 无法解析（CS0246/CS0759） |

**已知缺陷修复记录**（2026-09-14）：permissions.xCodeGen.json 绝对式路径 bug；Approval 缺配置（4-5 天陈旧 + 缺 Conditions）；EntityEmpty.cshtml 模板缺 `using System.Collections.Generic`（源码 `_TKWF/_xCodeGen/xCodeGen.Cli/Templates/` + 部署 `%TKWFDeployPath%/xCodeGen/Templates/` 双修）。
> **DtoEmpty 模板缺陷（2026-10-01 MFA 引导发现）**：DTO 骨架 `.g.cs` 生成侧同样缺 `using System.Collections.Generic;`（`List<ValidationResult>` 解析失败 CS0246）——MFA 3 个 DTO `.cs` 骨架已手工补 using；模板源（`DtoEmpty.cshtml`）待框架组随 xCodeGen 迭代修复（记录待报，勿重复手工补丁）。

### 配置分层原则（领域自治，2026-10-06 用户裁定）

> **背景**：认证中心内建标准对内端点（登录/refresh/logout/票据换令牌等）+ 联邦对外端点（`/sso/*`）设计时裁定——**端点开关等配置按领域自治原则分层**，避免领域决策泄露与安全隐患。

**分层模型**：
- **领域决策**（Domain 配置节，如 `TKWF:AuthCenter`——Initializer/Options）：能力开关（`EnabledAuthTypes` fail-closed）、安全策略（登录保护频控/令牌生命周期/票据白名单/密钥路径）——领域行为经门面 `User.Use<接口>()` AOP 帧内执行。
- **表现层暴露面**（Web 配置节，如 `TKWF:AuthCenter:Web`——WebExtension Options）：路由前缀 `RoutePrefix`、端点 HTTP 映射开关（**仅暴露面，不改变领域能力**）、中间件锚点/挂载。

**判定准则**：开关改变**领域认证行为/安全策略** → 领域配置（Web 层不得覆盖）；开关只控 **HTTP 暴露面** → 表现层配置（装配时按需调整）。

**安全红线**：
1. Web 钩子仅宿主注册（G18 §3）——业务逻辑经 `User.Use<门面>()` AOP 帧内执行，Web 层不持有领域决策
2. 表现层不可关闭领域安全行为（`EnabledAuthTypes` fail-closed 领域层强制，Web 配置**不存在**对应开关——防"Web 层关掉短信频控/口令校验"类绕过）
3. Web Options 是 POCO——只承载暴露面，不镜像领域配置（防双源）
4. 两类配置解耦可测：领域配置变更影响行为（Domain 层测试锁定）；Web 配置变更只影响路由/暴露面（Web 冒烟锁定）

**倒查待办（2026-10-06 登记）**：现有扩展设计择机按此原则倒查（如 FeatureManagement Provider 链分层、RateLimiting 分区、Notifications 偏好路由、Settings 分层读写等）——凡"表现层可开关领域安全行为"处需整改。

**先例落地**：AuthCenter 对内端点（`AuthCenterWebOptions` `TKWF:AuthCenter:Web`）+ Federation 对外端点（`FederationWebOptions` `TKWF:Federation:Web`）均按此分层设计（2026-10-06 迭代）；领域侧 `AuthCenterOptions`（`TKWF:AuthCenter`）/`FederationOptions`（`TKWF:Federation`）承载能力/安全决策。

### 构建/编译操作纪律

- **dll 被占用（`CS2012`/`file in use by another process`）时，用 `dotnet build-server shutdown` 优雅关闭 MSBuild/VBCSCompiler 编译服务器**，而非强杀进程——编译服务器是常驻进程（MSBuild node + Roslyn compiler server），强杀会留下孤儿进程/状态损坏；shutdown 后重试构建即可。若 shutdown 后仍占用，再检查是否残留 dotnet 测试宿主进程。

---

## 变更记录

> 完整历史时间线（2026-08-30 起，各扩展模块 + 全局机制变更）见 [docs/变更记录.md](./docs/变更记录.md)——2026-10-05 移出（AGENTS.md 被 OpenCode 自动加载，超长记录占上下文 ~50%，用户裁定合并一条时间线）。此处仅保留最近 3 条。

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-10-07 | — | **✅ 多通道联邦 Phase 1 实施闭环（v0.3.0——方案 Oracle PASS，41 项目 1888 用例全绿）**：**① Federation 主包核心契约层**（8 新文件）——`ChannelConfig`（公共列 ChannelId/PlatformType/AppId/AppSecret + Extra 扩展字典 + IsDefault/IsEnabled）+ `IChannelRegistry`/`StaticChannelRegistry`/`CompositeChannelRegistry`（组合语义单点：DB 优先→静态回退，Phase 1 仅静态，P2 注入零返工）+ `IChannelSource`（平台库投影契约）+ `ISsoChannelFactory`/`SsoChannelFactory`（按 channelId 构造，帧内 User 供给 IDomainUser——Oracle M8）+ `ISsoLogin`/`SsoLogin`（编排门面 + 默认降级）+ `FederationStaticChannelOptions`；**② 6 平台库收敛**——14 处 `Channels.FirstOrDefault()` 全消除（WeChat/QQ/DingTalk/WeCom/Oidc/Google/Microsoft）：通道 ctor 改收 `ChannelConfig?`（工厂预取，POCO 规避 DI004/DomainHost——实现修订 M1）+ 各库新增 `{Platform}ChannelSource`（IChannelSource 投影：AppId/AppSecret 公共列 + Token/AESKey/CorpId/EnableUnionId 等平台特有进 Extra——M7 映射约束）+ 注册扩展补 IChannelSource 行；OIDC 系异构：`OidcChannelBase` ctor 收 ChannelConfig + `EffectiveConfig` 虚方法从 Extra 重建（`OidcChannelConfigKeys` 键常量 + FromChannel）+ 派生（Google/Microsoft/OidcConfiguredChannel）同步，复合编码/tenant 模板语义保留；**③ Template 四件套同步**（Oauth/Event 收 ChannelConfig + 新增 {Platform}ChannelSource 模板 + 注册扩展补行——未来平台复制即多通道）；**④ 测试宿主**：7 库测试项目补 5 行 Federation 核心门面注册（平台库测试宿主模拟"已启用 Federation"最小生产等价面）+ Setup 改 `await user.Use<ISsoChannelFactory>().CreateAsync(channelId, channelType)`；**⑤ 验收**：grep `FirstOrDefault` 平台库零命中、F8 slnx 构建 0 错误 + 41 项目 1888 用例全绿（QqApiClient/DingTalkApiClient/WeComAuthorizeUrlBuilder 等出站客户端自持凭证解析机制不动——Oracle P2-4 凭证自持边界）；文档同步（联邦互联使用指南 §3.5 多通道联邦快速开始 + 平台网关模板文档组件 8.5/§2.2 更新） |
| 2026-10-07 | — | **✅ AuthCenter V0.9.0 身份域重构与密码能力实施闭环（ADR-AuthCenter-身份域数据模型与密码能力边界 落地 + Oracle(oracle4) PASS WITH CONDITIONS）**：**① 身份域重构（A.1-A.8）**——凭据/档案表级分离（`AuthAccount` 瘦身凭据白名单 10 列 + 新 `UserProfileEntity` 1:1 档案（Nickname/Avatar/Birthday/Gender/Email），表名 `TKWF_AuthAccount`/`TKWF_UserProfile`（ADR100 表前缀批次——仅 AuthCenter 表，用户裁定））；`AuthLevel` 泛化（`Phone=1`/`Federated=2`——删 `Teacher=3`，`Wechat` 改名 `Federated`）；`teacher_verified` claim 移除（`TokenIssueRequest`/`TokenValidationResult`/`VerifyResponse` 删字段 + 4 构造点 + `/verify` 响应）；联邦 Id 归一化（删 AuthAccount 微信 3 列 + 2 UX → `WeChatAuthenticationProvider` 改走 `ISsoChannelMapService`（wechat_mp/wechat_web channel）+ `PlatformAccountMapService.LinkAsync` 硬化（UX 双索引冲突 catch 重查）；`IsWechatBound` 改查通道行）；**② 密码三件套（B.9-B.11）**——`PasswordAuthenticationProvider`（主框架 `PasswordHasher` PBKDF2）+ `IPasswordLoginService`（镜像 Sms 门面）+ `IAuthAccountService.SetPasswordAsync`/`ChangePasswordAsync`（DataService `EntityUpdateWhereAsync` CAS 字段级写 + TokenVersion++，P1-4 顺带迁移 `IncrementTokenVersionAsync` CAS）+ `IPasswordResetService`（SMS `SmsScenes.Reset` 现成/Email `IEmailSender` 可空降级（新增 Emailing.Abstractions 引用）/扫码 OAuthTicket 前置——UId-keyed 不实现 `IAccountPasswordManager` 第二实现 + `PasswordResetCodeEntity` 底册自带投递（B.11））；频控走 `AuthLoginAttempt` COUNT（P0-1 方案 A——`IRateLimitCheck` defer v4.10.67+）；**③ 其他**——`EnabledAuthTypes` fail-closed 生效（P1-1/P2-6——先过滤再选区，默认值改 `["sms","wechat","password"]`）；`IUserProfileSource` 标 `[Obsolete]`（C.14——V0.9.0 保留实现改读 UserProfile，V1.0.0 移除）；`VerifyResponse.AuthLevel` 加 `[JsonPropertyName("auth_level")]`（P1-2 JSON 键名对齐）；xeCodeGen 生成物重生成（11 实体含 UserProfile/PasswordResetCode——破局 xCodeGen 依赖编译 DLL 死循环：编译缓冲→新 DLL→`--force` 重生成）；测试 **134 用例全绿**（123 既有适配 + `PasswordCapabilityTests` 11）+ 全 slnx 41 项目 **1869 用例全量回归零失败**（Federation 23 同步适配 UserProfile 档案）；文档收尾（README 版本/契约/实体表/组件清单/演进 + 使用指南 V0.9.0 密码段 + 根 README 基线） |
| 2026-10-07 | — | **✅ V5 Iter-3/4 能力/依赖声明批量补全（5 实体型扩展——Permissions/Account 试点后第二批）**：`[TKWFExtensionCapability]`（Iter-3 M1 能力目录）——AuthCenter→ITokenService / Federation→IToken2Service / FileManagement→IFileManager / Identity→IUserManager / Notifications→INotificationPublisher（均 `FullIQueryable`——实体型 DataService LINQ）+ 5 处 `_CapabilityCatalog.g.cs` 生成物确认（CapabilityEntry 含扩展名/门面/空 DependsOn/版本线——P4 无信号零噪音，仅标注扩展生成）；`[TKWFExtensionDependency]`（Iter-4 M3 依赖版本门控）——实测 csproj ProjectReference 9 条跨扩展 Abstractions 边补 8 条（Account 试点已有）：AuthCenter→UserCenter.Abstractions（IUserProfileSource 0.1.0）/ Federation→AuthCenter.Abstractions（ISsoAccountQueryService 0.1.0）/ FileManagement→BlobStoring.Abstractions（IBlobStorageService 0.1.1）/ Identity→Account+Permissions.Abstractions（IAccountPasswordManager 0.1.2 + IPermissionChecker 0.2.1）/ Notifications→Emailing+Permissions.Abstractions（IEmailSender 0.1.1 + IPermissionBatchChecker 0.2.1）；MinVersion 取各 Abstractions 已发布最新 tag；**Navigation 留待其迭代批次**（纯内存未引 `TKWF.CodeGeneration` analyzer——Iter-3 方案明确按迭代节奏无强制时限，避免强行接 analyzer 引入 DI001 诊断噪音）；补 `using TKW.Framework.CodeGeneration`（5 文件）；CAP/DEP 诊断零误报 + slnx 0 错误 + 全量回归 40 项目 1841 用例全绿（174b0db 提交 7e21835 后） |
| 2026-10-06 | — | **✅ N3 QQ 平台网关库协议实现闭环（Federation.QQ V0.1.0——T2/T3/T5/T6 落地，N1 骨架升级完成）**：`QqApiClient` 出站（`librarian` 官方 wiki 协议结构核实——token/me 端点默认非 JSON 恒传 `fmt=json` + `StripJsonp` JSONP 剥壳兜底 + `error` 数字字段双路解析 + get_user_info `ret/msg` 错误模型分端点；code→access_token→/me→openid 一次性链含 **redirect_uri 一致性校验（N3 P1-4 + OAuth RFC 6749 §4.1.3——出站显式携带 + QQ 服务端比对模拟 + 缺省拒发换取）** + **用户级 token 不缓存（N3 P1-5——QQ token=用户级 grant 非微信应用级）** + `GetMeAsync` unionid 开关（N3 P1-2——申请 100048 抛错，external_uid 恒=openid）+ get_user_info 裁剪降级 null）；`QqOauthChannel`（qq_oauth——code 缺拒 `QQ_CODE_REQUIRED` / redirect_uri 缺拒 `QQ_REDIRECT_URI_REQUIRED` / AuthLevel=2 / **不感知 state N3 P1-3 归装配层**）；`QqUserInfo`/`QqMeResult` 响应裁剪；`AddQqFederationChannels()`（T4 保留）+ 测试 16 用例全绿（生产路径通道正负 7 + QqApiClient 直构单测 8 + 协议结构断言——CapturingHandler 出站 URL 断言 fmt=json/redirect_uri + StripJsonp 剥壳 + unionid 开关）；README 技术规范 + `docs/Federation.QQ/QQ互联连接器-使用指南.md` + N3 方案状态更新 + 根 README（测试基线 1841/40 项目 + Federation.QQ 一行 + NuGet 段 7 平台库已发布最终态修正）；**F8 全量回归 slnx 构建 0 错误 + 40 项目 1841 用例全绿零失败** |
| 2026-10-06 | — | **✅ AuthCenter V0.8.0 发布上架（B2——run 37439864527）+ V5 Iter-3/4 联动（3038d40）+ OIDC 归并发布配套**：tag `AuthCenter/v0.8.0` 自红树 ae78e43（NU1605 失败）移绿树 3038d40 推送触发——build-test 全绿 + publish 推 **AuthCenter 0.8.0（stable）** + AuthCenter.Abstractions 0.1.1-preview.0.19 / Federation 0.2.1-preview.0.12 首发 + 全部已发布扩展新 preview；**⚠️ `TKWF.Federation.*` 7 平台库 403 阻塞**（nuget.org 账户权限仅覆盖 `TKWF.Ext.*` 前缀——待 nuget.org 侧补前缀后重触发补齐；QQ 骨架未发布，Permissions.Validation IsPackable=false 不发布）；CPM 全量 lockstep 4.10.65（解除 4.10.57 Analyzer 桥接）+ Permissions `[TKWFExtensionCapability]`/Account `[TKWFExtensionDependency]` 试点声明（Iter-3/4 端到端验证：`_CapabilityCatalog.g.cs` 生成 + DEP 零诊断）；A1/A2 unlist 复核（Permissions/Navigation 4.x 未列 15 个含 4.9.79——4.x 意外版本清理面，Listed 非目标原样零误伤）
| 2026-10-06 | — | **✅ OIDC 原语归并 Utility 引擎实施闭环（框架 v4.10.64 响应后执行扩展侧 T3/T4/T5——先本地部署消费部署根 4.10.64，CPM 4.10.61 桥接待框架发布后 lockstep）**：`Federation.Oidc` 删内联 `JwksManager.cs`/`OidcIdTokenValidator.cs`（引擎上移——kid 路由/墓园 Dispose/512KB 钳制/RSA≥2048/iss 通配正则/azp/leeway/nbf/exp 强校验）；`OidcAuthFlow` → `OidcChannelFlow` 薄层（authorize/code/userinfo 委托引擎 `OAuthClient` + 验签委托 `IdTokenDecoder.ValidateAsync` + 流自持 `JwksManager` 按 jwksUri 键缓存（L1 保留）+ **discovery 委托引擎 `OidcDiscoveryClient`**（v4.10.64 A 类边界扩展——容忍降级 null 语义保留）；保留内联仅 private_key_jwt P1-8）；csproj 补 `TKWF.Utility` 条件引用；Oidc 17（3 IsIssuerAllowed 单测改 2 集成）/Google 9/Microsoft 11 全绿 + 全量回归 39 项目零失败；README（Oidc V0.2.0 委托注记 + Google/Microsoft 配套）+ 归并方案变更记录 |
| 2026-10-06 | — | **✅ N1 平台网关通用模板实施闭环（Federation 国内总规划 L0 基建）**：模板文档 `docs/Federation/平台网关模板-开发指南.md`（权威固化——8 组件 + 出站-only 精简形态 + 可选第 9 组件 AuthorizeUrlBuilder（WeCom 先例）+ 依赖接线双模式 + 注册扩展固定形态 + 测试宿主 + 信任根分档 + 复制清单 30min 目标）+ 脚手架 `_Framework/Federation.Template/`（占位符 `{Platform}`/`{Namespace}`/`{platform}`——复制即改）+ 测试模板 `_Tests/Extension.Federation.Template.Tests/`（TestHostBase + IChannelProbe 探针门面生产路径枚举 + OAuth/信任根正负用例模板）+ **N3(QQ) 试点骨架**（`_Framework/Federation.QQ/` 出站-only——QqOptions/QqChannelConfig/QqApiClient/QqOauthChannel/QqUserInfo 骨架 + AddQqFederationChannels 壳 + QqTestHost 壳，协议实现在 N3 立项填充，两差异按 F5 权威定型：ChannelConfig 同 Options 文件/AuthorizeUrlBuilder 不作强制）；slnx 注册（Federation.QQ + QQ.Tests）+ **F2 验证 slnx 构建 0 错误**；N1 方案变更记录 + 立项依据状态表更新 |
