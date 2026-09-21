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
- **双模式引用开关（2026-09-16 方案 B 定案 DLL 模式，用户裁定）**：`UseLocalFw`（`Directory.Build.props` 集中定义，默认 `true`）——`true` = **DLL 模式**（本地联调：DLL 引用部署根 `$(TKWFDeployPath)\build\refs\`（对齐消费方 DMP-Lite，**零跨仓库源码耦合**）；`false` = **NuGet 发布**（PackageReference，CI/发布用，`ci-publish.yml` env `UseLocalFw: 'false'` 验证真实消费方视角）。实现：**不导入主框架 `TKWF.Domain.targets`**（其会无条件注入 build/refs 的 Analyzer，与扩展纯 Analyzer NuGet 包冲突——Roslyn 增量缓存 bug）；Directory.Build.props 自建集中 DLL 引用块（Core/Utility/Abstractions/Domain + 运行时传递依赖）+ 各扩展 csproj 按需 `Reference`（CodeGeneration.Abstractions/Domain.FreeSql/特殊包）。扩展 csproj 引用块按 `Condition="'$(UseLocalFw)' == 'true'"` 条件化；命令行 `-p:UseLocalFw=false` 覆盖。**CI Analytics 门控**：4.10.27（含 Utility.Analytics）发布前 `ANALYTICS_ENABLED=false` 排除 Analytics（nuget 模式 restore 失败），CI 用显式项目列表（slnx 不支持按属性排除项目）。**DLL 模式维护**：主框架源码构建后 `F:\TKWF_FRAMEWORK_PATH\build\refs\`（TKWFDeployPath）自动同步（源码树 refs 与部署根经 `_PushToRefs` 双写）——升级主框架包需重同步部署根。**Analytics 本地走 DLL 即可**（部署根 Utility 4.10.28 含 Utility.Analytics）。**⚠️ V4.10.32（A+ 阶段 4）SG Analyzer 版本锁**：SG1 分析器（`TKWF.CodeGeneration`）**恒用 NuGet 纯 Analyzer 包**（DLL 模式**不**从部署根 refs 挂 `<Analyzer>`——refs DLL 缺 Roslyn 依赖，且 AGENTS §6 裁定废弃该路径）。**破坏性后果**：主框架破坏性版本（如 v4.10.32 删旧桥/Data 类）发布后，扩展 DLL 模式（运行时用部署根 4.10.32 DLL + Analyzer 用 NuGet 旧包）会**编译失败**（旧 SG 生成物引用已删类型 / 新 Initializer 调 `CreateContributorInstances` 在旧 Abstractions 缺失）——**必须等主框架新包发布 + CPM 升级（`Directory.Packages.props`）后验证**。扩展侧 A+ 阶段 4 代码（3 Initializer 编译期化 + 39 .g.cs 死 using + xCodeGen 模板）已验证就绪，待 4.10.32 CPM 升级后全量验证。—— **✅ 状态闭环（2026-09-22）**：4.10.32 CPM 升级已全量验证（8ac0ff9，slnx 构建 0 错误 + 全量测试全绿）；**v4.10.33（i18n 国际化 Phase 1，零破坏性影响实证）CPM 升级亦已全量复验**——12 包 4.10.32→4.10.33（Directory.Packages.props），slnx 构建 0 错误 + 29 测试项目全绿（1385 用例），扩展侧零代码改动（新增 `TKWF.Framework.Localization` 主框架内部设施，扩展无需引用）。

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

> **实践思路**（V0.2.0 起）：扩展模块**可以**采用框架原生开发方式——引入 SG1 分析器 + xCodeGen 生成 DTO/DataService/Conditions/IDomainEntity 实现，与业务领域项目使用同一套开发模式（tkwf-entity / tkwf-service skill）。这使扩展获得自动建表、REST/GraphQL API 暴露、Dto 自动裁剪等框架能力，减少手写样板代码。
>
> **数据访问红线（2026-09-07 用户裁定）**——扩展数据访问层必须遵循以下三条，违反即架构违规：
> 1. **不允许直接使用 ORM（依赖）**——扩展代码不得注入 `IFreeSql`（或其他 ORM）直接写查询/SQL，除非业务有特殊需要且框架不可能满足（如 DataPort raw SQL 的 UTC DateTime 语义）；裸 ORM 无委托解析、autocommit、不参与 UoW，破坏事务一致性。
> 2. **不允许直接使用 `IEntityDAC<T>`**——扩展不得在自定义 Store/Service 中直接注入 `IEntityDAC<T>` 手写数据访问；须用 SG1/xCodeGen 生成的 DataService（`DomainDataServiceBase`/`DomainReadOnlyDataServiceBase` 派生，`[GenerateController(FromDataService=true)]` 暴露 API）。极少情况需征求用户同意并记录，且不能作为示范（Permissions `EntityDACPermissionStore` 即为反例，待整改为委托 DataService）。
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

### 构建/编译操作纪律

- **dll 被占用（`CS2012`/`file in use by another process`）时，用 `dotnet build-server shutdown` 优雅关闭 MSBuild/VBCSCompiler 编译服务器**，而非强杀进程——编译服务器是常驻进程（MSBuild node + Roslyn compiler server），强杀会留下孤儿进程/状态损坏；shutdown 后重试构建即可。若 shutdown 后仍占用，再检查是否残留 dotnet 测试宿主进程。

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-08-30 | v0.1.0 | 初始版本——对齐主仓库 Agents_TKWF.md 规范，适配扩展仓库独立版本/公开私有边界 |
| 2026-08-30 | — | §8 新增「扩展使用 SG1/xCodeGen（框架原生开发方式）」实践思路（V0.2.0 起） |
| 2026-09-01 | — | v4.9.85："发现即启用"改为"发现不自动启用"，消费方须 `[TKWFEnabledExtension]` 白名单声明；§8 补充白名单概念 |
| 2026-09-07 | — | §8 新增「构建/编译操作纪律」——dll 占用时用 `dotnet build-server shutdown` 优雅关闭编译服务器，不强杀进程 |
| 2026-09-07 | — | §8 新增「数据访问红线」（用户裁定 2026-09-07）——扩展禁裸 ORM / 禁直接 IEntityDAC / 应发挥组装优势；标准路径 = DataService → IEntityDAC → 实现层；待整改清单 10+1 扩展分批改造 |
| 2026-09-07 | — | §8 新增「跨表查询 VEntity」实践——多对一 JOIN 用 VEntity（ViewSql 双方言 + 手写只读 DataService + Initializer 手动注册 + 生产 DBA 建视图）；一对多主从聚合保持两步；先例 Identity/Notifications |
| 2026-09-10 | — | §8 新增「分布式事件 handler 注册机制」要点（FeatureManagement v0.3.0 先例）——扩展内建 `[DomainEventHandler]` + `IDistributedEventHandler<T>` handler **必须 public**（SG4 消费方编译期经 ReferencedAssemblySymbols 生成 `typeof(Handler)` 引用，internal 无 IVT → CS0122；public 构造器依赖类型亦不可 internal——CS0051）；Initializer 不手动注册；扩展自身构建不触发 EVT003/EVT004（消费方 WebApi 编译时执行） |
| 2026-09-14 | — | §8 新增「xCodeGen 生成物管理（.g.cs 入库政策）」——生成物入库（源码库自包含）；修复 permissions 配置绝对路径 bug（OutputDir 相对 OutputRoot 解析）；metrics-tests 配置先例（测试宿主走标准管线 + 生成 ProjectMetaContext）；重生成纪律 |
| 2026-09-14 | — | §8 新增「扩展模块生成物健康自检清单」7 项（配置 1:1 / 相对路径 / 重生成纪律 / 时间戳新鲜 / 分部对 / 宿主生成上下文 / 骨架编译）+ 已知缺陷记录——审计固话（Approval 缺配置 4-5 天陈旧 + EntityEmpty 模板缺 using 双修）；经验归入主框架私有《扩展模块生成物与测试宿主规范》 |
| 2026-09-16 | — | §1 新增「维护/提交归属裁定」（用户 2026-09-16）：**主框架 `_TKWF/docs/03_扩展模块/` 全目录 + 主框架文档中扩展模块状态段落由本仓库负责维护与提交**（含 push 不打 tag，不依赖框架组）——因本仓库是 public（扩展模块内容对外可见、随扩展演进），主框架仅作物理承载；§3 流程 6/7 补充提交归属注记 |
| 2026-09-16 | — | §6 新增「双模式引用开关」（用户裁定方案 B）——`UseLocalFw` 集中开关（默认 true=本地 ProjectReference 联调 / false=nuget 发布 CI 用）；Analytics csproj 条件化引用先例（4.10.27 前 ProjectReference，发布后切 nuget）+ CI `ANALYTICS_ENABLED` 门控 |
| 2026-09-22 | — | §6 CPM 主框架包 4.10.32 → **4.10.33**（i18n 国际化 Phase 1，主框架零破坏性影响实证）——12 包版本同步提升；slnx 构建 0 错误 + 29 测试项目全绿（1385 用例）复验通过；扩展侧零代码改动（新增 `TKWF.Framework.Localization` 主框架内部设施，扩展无需引用）；§6 注记补「✅ 状态闭环」 |
