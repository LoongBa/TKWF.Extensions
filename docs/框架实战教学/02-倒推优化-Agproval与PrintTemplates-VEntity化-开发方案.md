# 02-倒推优化开发方案：Approval 与 PrintTemplates 两步查询 VEntity 化

> **系列**：框架实战教学（开篇见 [`01-扩展模块最优解探索-Schema级数据组合-开篇.md`](./01-扩展模块最优解探索-Schema级数据组合-开篇.md)）
> **案例定位**：倒推路线图（§八）首个 🟢 优先落地案例——**纯投影聚合优先 VEntity**（F2 措辞澄清的实证）
> **涉及扩展**：`TKWF.Ext.Approval`（当前 V0.2.2）→ 目标 V0.3.0；`TKWF.Ext.PrintTemplates`（当前 V0.1.2）→ 目标 V0.2.0
> **状态**：✅ 已实施（oracle3 PASS WITH CONDITIONS——C1-C4 裁定通过；评审修订 C-high-1/2 + C-med-3/4 已采纳，C-low-5/6 实施时补全，P1-P5 已纳入；2026-10-01 实施完成）
> **版本**：v0.1.2

---

## 一、目的与目标

对 Approval 与 PrintTemplates 两个扩展的**纯读查询路径**执行 VEntity 化改造。**两案收益不同（oracle3 C-high-1 修订）**：

1. **PrintTemplates（真消除往返）**：`模板→版本` 两步查询（2 次 DB 往返）合并为**单查询 JOIN 下推 DB**（`vw_PrintTemplateVersionView` 携带 `Key`/`TemplateName`/`Content`）——`RenderAsync` 从 2 次往返降为 1 次，并补足当前返回面缺失的模板名（免第 3 次往返）。
2. **Approval（任务链查询 VEntity 化 + 投影下沉）**：`GetInstanceDetailAsync` 的任务链查询（第 2 步 L70-72）改为 `vw_ApprovalTaskView` JOIN 下推（携带 Instance 列）；**实例查询（第 1 步）保留**——C2 裁定 `BusinessDataJson` 大字段须取全量，故净查询数不变（仍 2 次）。收益为投影下沉（任务链携带者 BusinessType/BusinessId/InstanceStatus 等 Instance 列）+ 读模型联邦 + 为跨表过滤查询铺路。
3. **VEntity DTO 返回面（用户已裁定）**：遵循 2026-09-30"当前内部测试、无历史负担——直接采用最优形态，不留兼容双轨"。PrintTemplates 返回类型从实体升至视图实体（**公开契约签名变更，用户已裁定接受**）；测试宿主随仓库同步升级。

**验收**：PrintTemplates 读路径全部单查询（1 往返）；Approval 任务链查询单查询下推（无 `EntitySelectAsync` 第二次往返，实例查询保留）；`ApprovalManager` 写路径与 `TemplateManager` 写路径 **零触碰**；既有测试断言全绿（锚点见 §六）；新增 VEntity 用例。

---

## 二、现状分析（精确到文件:行）

### 2.1 Approval —— `GetInstanceDetailAsync` 两步查询

`ApprovalQueryService.cs` L64-94（**纯读聚合，唯一 VEntity 化目标**）：

```csharp
public async Task<ApprovalInstanceDetailDto?> GetInstanceDetailAsync(long instanceId, CancellationToken ct = default)
{
    var instance = await instanceDataService.EntityGetAsync(i => i.Id == instanceId, ct);   // L66 第 1 步：实例单表
    if (instance == null) return null;

    var tasks = await taskDataService.EntitySelectAsync(                                    // L70-72 第 2 步：任务链单表
        t => t.InstanceId == instanceId, 0, 10000,
        q => q.OrderBy(t => t.StepIndex).ThenBy(t => t.Id), ct);

    return new ApprovalInstanceDetailDto { ...16 字段..., Tasks = tasks.Select(MapToTaskListItemDto).ToList() };
}
```

- **两次往返**：`ApprovalInstance`（含 `BusinessDataJson` 大字段）+ `ApprovalTask`（22 列）。
- **同文件另两处查询** `GetInstancesAsync`（L20-42）/`GetPendingTasksAsync`（L45-61）已是 **DB 级单表分页**（`CountAsync` + `EntitySelectAsync` skip/take 下推）——**不动**。
- **`ApprovalManager` 写路径查询**（oracle3 C-med-3 修正：5 处任务链聚合 + 1 处 CC 事件查询）：
  - **5 处任务链聚合**（L275/L409/L579/L640/L723，`taskDataService.EntitySelectAsync`）——批量置 Completed/加签去重/超时跳转，**需实体回写**，VEntity 只读不适用——**不动**
  - **1 处 CC 事件查询**（L856 `RaiseCcEventAsync` 内 `ccDataService.EntitySelectAsync`，ApprovalCCEntity）——post-commit 事件 payload 组装，**只读无回写**，但非任务链聚合亦非读模型聚合——**不动**
  - 全部非 VEntity 候选，方案明确划界。

### 2.2 PrintTemplates —— 三步查询

`TemplateManager.cs`：`GetVersionAsync`（L31-36）/`GetActiveVersionAsync`（L39-44）/`ListVersionsAsync`（L47-52）——三步模式：

```csharp
public async Task<PrintTemplateVersionEntity?> GetVersionAsync(string key, string version, CancellationToken ct = default)
{
    var template = await _store.GetByKeyAsync(key, ct);           // 第 1 步：Key → TemplateId
    if (template == null) return null;
    return await _store.GetVersionAsync(template.Id, version, ct); // 第 2 步：TemplateId+Version → Version
}
```

- **但实际是 2 次往返**（`TemplateStore` 的 `GetVersionAsync` 内部再按 TemplateId 查版本表——见 bg_c3d6fa47 调研：`GetVersionAsync` L37-38 委托 `GetByTemplateAndVersionAsync`）。`RenderAsync`（L55-71）每次渲染本路径 = **2 次往返**。
- **返回面**：返回 `PrintTemplateVersionEntity`（实体，**不含模板 Key/Name**）——消费方需模板名须第 3 次调用 `GetTemplateAsync`。
- **写路径** `PublishAsync`（L74-119）/`DraftAsync`（L122-167）/`ArchiveAsync`（L170-182）——需实体回写（Status/Content 变更）——**不动**。

---

## 三、优化设计

### 3.1 `vw_ApprovalTaskView`（JOIN `ApprovalTask` → `ApprovalInstance`）

- **JOIN 键**：`ApprovalTask.InstanceId = ApprovalInstance.Id`（INNER，多对一，行数不变、`Task.Id` PK 透传唯一稳定——符合 AGENTS §8 VEntity 判据）。
- **建议投影列**（21 列，对齐详情面 + 列表过滤键；`BusinessDataJson` **排除**——大字段进 JOIN 视图是性能反模式，详情场景保留单表 `EntityGetAsync` 取全量）：

| # | 列 | 来源 | 说明 |
|---|---|---|---|
| 1 | `Id` | Task.Id | PK 透传（`IsPrimary=true`） |
| 2 | `InstanceId` | Task.InstanceId | 外层过滤键 |
| 3 | `StepIndex` | Task | 排序主键 |
| 4 | `StepName` | Task | |
| 5 | `ApproverType` | Task | |
| 6 | `ApproverValue` | Task | |
| 7 | `ApproverUserId` | Task | 待办查询过滤键 |
| 8 | `Status` | Task | |
| 9 | `ApprovedAt` | Task | |
| 10 | `ApprovedBy` | Task | |
| 11 | `Comment` | Task | |
| 12 | `TransferredTo` | Task | |
| 13 | `TaskCreateTime` | Task.CreateTime | 排序次键 |
| 14 | `BusinessType` | Instance | 业务过滤键 |
| 15 | `BusinessId` | Instance | 业务过滤键 |
| 16 | `InstanceStatus` | Instance.Status | 详情展示 |
| 17 | `IsActive` | Instance | |
| 18 | `CurrentStepIndex` | Instance | |
| 19 | `Submitter` | Instance | |
| 20 | `FlowCode` | Instance | |
| 21 | `InstanceCreateTime` | Instance.CreateTime | |

- **替代**：`GetInstanceDetailAsync` L70-72 任务链查询 → `ApprovalTaskViewDataService.SelectAsync(v => v, predicate: v => v.InstanceId == instanceId, orderBy: q => q.OrderBy(v => v.StepIndex).ThenBy(v => v.Id))` 单查询。
- **型写范式**（对齐 Identity/Notifications 先例）：

```csharp
[Table(Name = "vw_ApprovalTaskView", DisableSyncStructure = true)]
[DomainGenerateCode(IsView = true,
    ViewSql = @"CREATE OR REPLACE VIEW ""vw_ApprovalTaskView"" AS
SELECT t.""Id"", t.""InstanceId"", t.""StepIndex"", t.""StepName"", t.""ApproverType"", t.""ApproverValue"",
       t.""ApproverUserId"", t.""Status"", t.""ApprovedAt"", t.""ApprovedBy"", t.""Comment"", t.""TransferredTo"",
       t.""CreateTime"" AS ""TaskCreateTime"", i.""BusinessType"", i.""BusinessId"", i.""Status"" AS ""InstanceStatus"",
       i.""IsActive"", i.""CurrentStepIndex"", i.""Submitter"", i.""FlowCode"", i.""CreateTime"" AS ""InstanceCreateTime""
FROM ""ApprovalTask"" t
INNER JOIN ""ApprovalInstance"" i ON t.""InstanceId"" = i.""Id""",
    ViewSqlSQLite = @"CREATE VIEW IF NOT EXISTS ""vw_ApprovalTaskView"" AS
SELECT t.""Id"", t.""InstanceId"", ...（同上，双引号不变）...",
    ExposeGraphqlQuery = false,   // ⚠️ C4：敏感视图显式关闭（默认 true，不关即裸暴露）
    DefaultPageSize = 50)]
public partial class ApprovalTaskView { }

// 手写只读 DataService（xCodeGen 跳过 VEntity 模板 Engine.cs L42-46；ADR61 自动注册，无需 Initializer 手动 TryAddScoped）
partial class ApprovalTaskViewDataService(IDomainUser user, IEntityReadOnlyDAC<ApprovalTaskView> dac)
    : DomainReadOnlyDataServiceBase<ApprovalTaskView, ApprovalTaskViewDto>(user, dac, hasSoftDelete: false)
{
    public async Task<List<ApprovalTaskView>> GetTasksByInstanceIdAsync(long instanceId, CancellationToken ct = default)
        => await SelectAsync(v => v, predicate: v => v.InstanceId == instanceId,
            orderBy: q => q.OrderBy(v => v.StepIndex).ThenBy(v => v.Id), limit: 10000, ct: ct);
}
```

### 3.2 `vw_PrintTemplateVersionView`（JOIN `PrintTemplateVersion` → `PrintTemplate`）

- **JOIN 键**：`PrintTemplateVersion.TemplateId = PrintTemplate.Id`（INNER，多对一，`Version.Id` PK 透传）。
- **建议投影列**（12 列，含**核心收益** `Key`/`TemplateName`——当前返回面缺失）：

| # | 列 | 来源 | 说明 |
|---|---|---|---|
| 1 | `Id` | Version.Id | PK 透传（`IsPrimary=true`） |
| 2 | `TemplateId` | Version.TemplateId | |
| 3 | `Key` | **Template.Key** | **核心收益**——外层过滤键（替代第 1 步 `GetByKeyAsync`） |
| 4 | `TemplateName` | **Template.Name** | **核心收益**——当前返回面缺失 |
| 5 | `TemplateDescription` | Template.Description | 可选 |
| 6 | `Version` | Version.Version | 精确版本过滤键 |
| 7 | `Content` | Version.Content | 渲染正文（大字段，但渲染必需） |
| 8 | `Status` | Version.Status | Active 过滤键 |
| 9 | `Description` | Version.Description | |
| 10 | `PublishedAt` | Version.PublishedAt | |
| 11 | `PublishedBy` | Version.PublishedBy | |
| 12 | `CreateTime` | Version.CreateTime | |

- **替代**（RenderAsync 从 2 次往返降为 1 次）：
  - `GetVersionAsync(key, version)` → `SelectAsync(v => v, predicate: v => v.Key == key && v.Version == version, limit: 1)`
  - `GetActiveVersionAsync(key)` → `SelectAsync(v => v, predicate: v => v.Key == key && v.Status == Active, limit: 1)`
  - `ListVersionsAsync(key)` → `SelectAsync(v => v, predicate: v => v.Key == key, orderBy: q => q.OrderByDescending(v => v.Version))`
- **⚠️ 关键差异**：现有返回类型是 `PrintTemplateVersionEntity`（实体），VEntity 化后返回 `PrintTemplateVersionView`（视图实体）——**`ITemplateManager` 公开契约签名变更**。用户已裁定"内部测试无历史负担，直接采用最优形态，接受签名变更"（§8.3 认知修正）；`RenderAsync` 内部只读 `ver.Content`，改动最小。

---

## 四、裁定点（需评审确认）

| # | 裁定点 | 建议 | 理由 |
|---|--------|------|------|
| **C1** | Approval：v0.2.0 新增的 **9 个委派/超时字段**（`DelegationState/OriginalAssigneeId/DelegatedToUserId/DelegatedAt/TimeoutAt/TimeoutAction/TimeoutTransferToUserId/TimeoutJumpToStepIndex/TimeoutProcessed`）是否进 `vw_ApprovalTaskView`？ | **进视图**（但 DTO 面暂不扩） | 视图是读模型快照，字段齐备为未来详情页扩展留余地（委派/超时状态展示是 v0.3.0 候选）；DTO 面保持现 13 字段，消费方零面变化（映射 View→现 DTO 字段即可） |
| **C2** | Approval：`BusinessDataJson` 大字段是否进视图？ | **排除** | `StringLength=-1` 大字段进 JOIN 视图 = 列表/JOIN 场景性能反模式；详情场景保留单表 `EntityGetAsync` 取全量（`GetInstanceDetailAsync` 第 1 步查实例不动） |
| **C3** | PrintTemplates：`ITemplateManager` 返回类型从 `PrintTemplateVersionEntity` → `PrintTemplateVersionView`？ | **变更** | 用户已裁定"内部测试无历史负担、直接采用最优形态"（§8.3）；不保留旧接口 = 不留兼容双轨。测试宿主随仓库同步升级 |
| **C4** | 两视图的 `ExposeGraphqlQuery` 策略？ | **显式 `false`（敏感视图经门面）** | **VEntity 默认 `ExposeGraphqlQuery = true`**（`QueryExposureDefaults.cs`：`ResolveGraphql → IsView(entity)`）——不显式关闭即裸暴露。`vw_ApprovalTaskView` 含审批人明细/评论（ApproverUserId/Comment/ApproveBy）与 `vw_PrintTemplateVersionView` 含模板正文（Content，商业资产）——敏感数据经 GraphQL 直连绕过业务鉴权（如审批"仅本人/审批人"语义、Active 版本语义），且框架当前**无"仅本人"过滤**（IGlobalQueryFilter 仅 TenantGlobalQueryFilter，OwnerFilter 为 §九 F4 候选）。**裁定：两视图显式 `false`，数据访问统一经既有门面**（IApprovalQueryService/ITemplateManager，门面内已有状态机/权限语义）——与 UserCenter C3 方案 B（组合视图经门面暴露）一致模式；未来需 GraphQL 直读时按 F4/安全评估再开。**oracle3 C-med-4 观察**：Notifications `UserNotificationView` 的 `ExposeGraphqlQuery=true` 实为 UserCenter C3 IDOR 分析**前**的既有决策，其收件箱私密性（UserId/State/ReadTime/Name）与本方案同属敏感面——本方案 false 是对先例的**收紧而非差异**；Notifications 先例列入 §九 F4（OwnerFilter）+ F11（默认收敛）候选的**回溯审查清单** |

---

## 五、影响面与写路径划界

| 面 | 变更 | 说明 |
|----|------|------|
| Approval 读路径 | `ApprovalQueryService.GetInstanceDetailAsync` 任务链查询 → VEntity 单查询 | 实例第 1 步保留（取 `BusinessDataJson` 全量）；净查询数不变（仍 2 次往返），收益为投影下沉 + 读模型联邦（oracle3 C-high-1） |
| Approval 写路径 | **零触碰** | **5 处任务链聚合**（L275/L409/L579/L640/L723，批量置 Completed/加签去重/超时跳转）+ **1 处 CC 事件查询**（L856，post-commit 只读 payload）——前 5 处需实体回写，VEntity 只读不适用；L856 非读模型聚合（oracle3 C-med-3） |
| Approval DTO 面 | 保持不变 | `ApprovalTaskListItemDto` 仍 13 字段；View→DTO 映射在 QueryService 内 |
| PrintTemplates 读路径 | `GetVersionAsync`/`GetActiveVersionAsync`/`ListVersionsAsync`/`RenderAsync` → VEntity 单查询 | 返回类型变更（C3）；**读方法从 TemplateManager 直接注入 `PrintTemplateVersionViewDataService`（不再经 ITemplateStore 读方法）**——消除 Store 中间层 + 视图列可见 |
| PrintTemplates 写路径 | **零触碰** | `PublishAsync`/`DraftAsync`/`ArchiveAsync` 需实体回写（Status/Content 变更），仍经 ITemplateStore 的 `GetByKeyAsync`/`GetVersionAsync(templateId, version)`/`ListVersionsAsync(templateId)` 实体读——**Store 实体读方法保留不动**（oracle3 C-high-2：写路径强依赖实体回写，改视图实体不可行——VEntity 只读禁写） |
| 生产部署 | 新增文档章节 | 两视图 ViewSql（PG + SQLite 双方言）写入使用指南；**生产需 DBA 手动执行 ViewSql**（SyncViewsAsync 只跑开发环境建视图，框架 F4 候选——跨扩展 DBA 建视图规范） |

---

## 六、测试锚点与新增用例

### 既有断言须保持全绿（VEntity 化后不得破坏）

- **Approval `D10_GetInstanceDetailAsync_ShouldIncludeTasks`**（ApprovalEngineTests.cs L566-584）：断言 `detail.Id`、`detail.BusinessDataJson == "{\"amount\":100}"`、`Assert.Single(detail.Tasks)`。
- **Approval `D12_*_DBPagination`**（L606-655）：断言 `Total` 独立 count + 页切片 + **按 Id 倒序**（`Items[0].Id == 3, Items[1].Id == 2`）——`GetInstancesAsync` 不动，天然保留。
- **PrintTemplates `TemplateManagerTests`**（L56-152）：断言 `Version`/`Status`/`Content`——VEntity 化后视图实体同字段，断言不变。
- **`FreeSqlTemplateStoreTests`**（L17-209）：Store 层直测——Store 的实体读方法（`GetVersionAsync(templateId, version)`/`GetActiveVersionAsync(templateId)`/`ListVersionsAsync(templateId)`）**保留但仅写路径复用**（§五），测试断言不变（这些方法签名不变，仍返回实体）；**TemplateManager 公开读 API 迁至视图后，Manager 层测试改走视图**（N3/N4/N5 覆盖）

### 新增用例

| # | 用例 | 验证点 |
|---|------|--------|
| N1 | VEntity 视图查询：按 InstanceId 返回任务链，StepIndex 升序 | Join 下推 + 排序 |
| N2 | VEntity 视图含 Instance 列（BusinessType/BusinessId/InstanceStatus） | 读模型联邦 |
| N3 | `GetVersionAsync(key, version)` 单查询返回视图实体（含 Key/TemplateName） | 两步→一步 |
| N4 | `GetActiveVersionAsync(key)` 返回 Active 版本 | 状态过滤下推 |
| N5 | `RenderAsync` 渲染路径单查询（内部 GetActiveVersionAsync 走视图） | 2 往返→1 往返 |

---

## 七、实施清单

1. **Approval**：
   - 新增 `ApprovalTaskView.cs`（`[Table("vw_ApprovalTaskView")]` + `[DomainGenerateCode(IsView=true, ViewSql, ViewSqlSQLite, ExposeGraphqlQuery=false)]` + partial class——**C4**）
   - 新增 `DataServices/ApprovalTaskViewDataService.cs` 手写只读 DataService（`DomainReadOnlyDataServiceBase<ApprovalTaskView, ApprovalTaskViewDto>` + `IEntityReadOnlyDAC<ApprovalTaskView>`，红线合规；不标 `[GenerateController(FromDataService=true)]`——REST 经 IApprovalQueryService 门面暴露）
   - 改 `ApprovalQueryService.GetInstanceDetailAsync`：任务链查询 → 视图单查询 + View→DTO 映射
   - ✅ `.xCodeGen/extensions/approval.xCodeGen.json` **已存在**（Scope="Entity" 全量扫描 + TargetProject 正确）——新实体被自动发现，**无需新建配置**（仅需重跑 `run-xcodegen.ps1 -Ext Approval` 提交新 `.g.cs`）
2. **PrintTemplates**：
   - 新增 `PrintTemplateVersionView.cs` + `DataServices/PrintTemplateVersionViewDataService.cs`（同上范式）
   - 改 `ITemplateManager`/`TemplateManager`：三读方法 + `RenderAsync` 走视图；返回类型变更（C3）
   - Store 层实体读方法（`GetByKeyAsync`/`GetVersionAsync(templateId, version)`/`GetActiveVersionAsync(templateId)`/`ListVersionsAsync(templateId)`）**保留不动**（oracle3 C-high-2：TemplateManager 写路径 Publish/Draft/Archive 强依赖 Store 返回实体做 Status/Content 回写；若改返视图实体则 `UpsertVersionAsync` 编译失败——VEntity 只读禁写）——仅 TemplateManager 公开读 API（按 key）迁至 `PrintTemplateVersionViewDataService`
   - ✅ `.xCodeGen/extensions/printtemplates.xCodeGen.json` **已存在**（同上）——无需新建，重跑生成即可
3. **回归**：`dotnet build` slnx 0 错误 + 全量测试绿（29 项目 1394 用例基线）
4. **文档**：两扩展使用指南补"VEntity 化"章节（含 ViewSql 生产部署 DBA 要求）+ README 版本演进
5. **落档**：教学系列 §八 路线图更新（Approval/PrintTemplates 🟢 已完成）+ 本篇案例编号 02

---

## 八、验收标准

- [ ] `GetInstanceDetailAsync` 任务链查询单查询下推（无 `EntitySelectAsync` 第二次往返；实例第 1 步保留取 `BusinessDataJson` 全量，整体仍 2 次往返——验收以"任务链下推"为准，非"整体单查询"，oracle3 C-high-1）
- [ ] `GetVersionAsync`/`GetActiveVersionAsync`/`ListVersionsAsync`/`RenderAsync` 单查询（无 `GetByKeyAsync` 前置往返）
- [ ] 视图行返回含 `Key`/`TemplateName`（PrintTemplates）与 `BusinessType`/`InstanceStatus` 等 Instance 列（Approval）
- [ ] 写路径零触碰（ApprovalManager **5 处任务链聚合 + 1 处 CC 事件查询** + TemplateManager 3 写方法 diff 为空，oracle3 C-med-3）
- [ ] 既有断言全绿 + 新增 N1-N5 用例绿
- [ ] `lsp_diagnostics` 变更文件干净
- [ ] 使用指南补 VEntity 章节 + 生产 DBA 建视图要求（P4）
- [ ] 落 ADR（P3）：`ADR-Approval-敏感视图经门面暴露策略` + `ADR-PrintTemplates-敏感视图经门面暴露策略`（C4 决策——敏感读模型经门面鉴权，对照低敏先例）

---

## 九、实施前补全项（oracle3 C-low-5/6 + P1/P2/P5）

| # | 项 | 说明 |
|---|----|------|
| C-low-5 | §3.1 ViewSqlSQLite 补全全文（当前为占位符）+ 视图实体显式属性 | 对齐 `UserRoleView.cs` 范式：`[Column(IsPrimary=true, Position=1)] public long Id { get; set; }` 等显式属性 + 列 Position；实施前必须补全 |
| C-low-6 | `ListVersionsAsync` 投影排除 Content（可选） | 列表场景无需渲染正文——或使用指南注明"ListVersions 返回含 Content，版本数大时考虑投影优化" |
| P1 | 视图实体属性类型一致 | Approval `DateTime TaskCreateTime`/`InstanceCreateTime`；PrintTemplates `DateTimeOffset` 列继承基表类型——类型不匹配致 FreeSql 映射异常 |
| P2 | `SelectAsync` 签名核对 | 参数名 `predicate`/`orderBy`/`limit`/`ct` 对齐 Identity `UserRoleViewDataService.GetRolesByUserIdAsync` 先例 |
| P5 | 测试宿主 ProjectMetaContext 核对 | 重跑 `run-xcodegen.ps1` 后核对 ApprovalTests/PrintTemplatesTests 宿主的 `OnRegisterInfrastructureServices` 返回 SG1 生成的 `ProjectMetaContext`（AGENTS §8 自检清单 #6）覆盖新 VEntity 自动注册 |

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-09-30 | v0.1.0-draft | 初始草案——基于 bg_c3d6fa47（Approval+PrintTemplates 细节探针）精确代码引用 + bg_6dbfbdb2（扩展实践摩擦）写路径划界确认 |
| 2026-09-30 | v0.1.1 | **oracle3 评审 PASS WITH CONDITIONS（`bg_59cc1308`）6 条件 + 5 建议全采纳**——修订 C-high-1（§一目标拆分：PrintTemplates 真消除往返 / Approval 任务链下推+投影下沉，实例查询保留净 2 次）+ C-high-2（§五/§六/§七 Store 实体读方法"保留不动"统一——写路径强依赖实体回写，改视图不可行）+ C-med-3（§2.1/§五/§八 ApprovalManager 6 处→5 处任务链聚合 + 1 处 CC 事件查询 L856）+ C-med-4（§四 C4 理由补 Notifications 先例回溯审查清单）；C-low-5/6（实施前补全项入 §九）+ P1-P5（§九 实施前补全项：SQLite ViewSql 全文/视图实体显式属性/投影排除 Content/类型一致/SelectAsync 签名核对/项目宿主 ProjectMetaContext/ADR 落档） |
| 2026-10-01 | v0.1.2 | **实施完成**——Approval v0.3.0 + PrintTemplates v0.2.0 全部落地：`vw_ApprovalTaskView`（21 列，JOIN 任务→实例）+ `vw_PrintTemplateVersionView`（12 列含 Key/TemplateName，JOIN 版本→模板）+ 两手写只读 DataService（`IEntityReadOnlyDAC` 红线合规）+ QueryService/ITemplateManager 改造（C3 返回类型变更）+ 测试宿主建 SQLite 真实视图 + N1-N5 用例；写路径零触碰（ApprovalManager 5 处任务链聚合 + 1 处 CC 事件查询 + TemplateManager 3 写方法 diff 为空）；ADR-Approval/PrintTemplates-敏感视图经门面暴露策略 落档；使用指南补 VEntity 章节 + 生产 DBA 要求；测试 Approval 72→74 / PrintTemplates 29→33 + 全量回归零失败；注：xCodeGen DtoEmpty 模板缺陷（骨架缺 `using System.Collections.Generic`）实施时手动修复，模板源待主框架双修（同 EntityEmpty 2026-09-14 先例） |

---

## 评审记录

| 日期 | 评审人 | 结论 | 修订 |
|------|--------|------|------|
| 2026-09-30 | oracle3 | **PASS WITH CONDITIONS**——裁定点 C1-C4 推理成立、与 AGENTS §8 VEntity 范式/红线/先例一致；6 条件（C-high-1/2、C-med-3/4、C-low-5/6）+ 5 建议（P1-P5） | C-high-1/2 + C-med-3/4 修订完成（§一/§二/§四/§五/§八）；C-low-5/6 + P1-P5 纳入 §九 实施前补全项 |