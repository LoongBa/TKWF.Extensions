# 04-倒推优化开发方案：OrganizationUnit 与 DataDictionary 内存拼装优化

> **系列**：框架实战教学（开篇见 [`01-扩展模块最优解探索-Schema级数据组合-开篇.md`](./01-扩展模块最优解探索-Schema级数据组合-开篇.md)）
> **案例定位**：倒推路线图（§八）**内存拼装/两步查询优化**——物化路径下推 + JOIN 读模型联邦（F2 措辞澄清：纯投影聚合优先 VEntity）
> **涉及扩展**：`TKWF.Ext.OrganizationUnit`（当前 V0.1.2）→ 目标 V0.2.0；`TKWF.Ext.DataDictionary`（当前 V0.1.2）→ 目标 V0.2.0
> **状态**：📋 已评审（**oracle3 PASS WITH CONDITIONS**——C-1~C-10 已修订，P1-P10 已纳入；H1/H2/H3/M1 实施前修正）
> **版本**：v0.1.1

---

## 一、目的与目标

对 OrganizationUnit 与 DataDictionary 两个扩展的**内存拼装/两步查询**执行**可下推部分 VEntity 化**——遵循"纯投影聚合（无业务逻辑）优先 VEntity；内存拼装用于有业务逻辑场景"（教学系列 F2 措辞澄清）判据：

1. **OU 可下推 3 处**（bg_2927440c 探针确认——物化路径 `Path` 字段已就绪）：
   - `GetSubTreeAsync(ouId)`：全量拉取 + 内存 `Path.StartsWith` 过滤 → **`WHERE Path LIKE '{ou.Path}%'` 下推**
   - `GetUserIdsInOrganizationUnitAsync(ouId)`：两步（子树 OU Id 集合 → junction 查 UserId）→ **JOIN OU→OUUser 单查询**（`vw_UserOrganizationUnitView`，README 已规划）
   - `GetAncestorsAsync(ouId)`：全量拉取 + 内存按 Code 匹配 → **`WHERE Code IN (path segments)` 下推**（需内存分段解析 Path）
2. **OU 必须留内存 1 处**：`GetTreeAsync`（全树递归组装——ParentId 分组 + AttachChildren）——不适用 VEntity。
3. **DataDictionary 可下推 1 处**：`GetOrLoadAggregateAsync(code)` 两步（定义 → 项）→ **JOIN Definition→Item 单查询**（`vw_DictionaryItemView`）；**BuildTree 递归留内存**（缓存仍有效——缓存聚合结果非查询）。
4. **FileManagement 排除**（探针确认）：`GetFolderTreeAsync` 全树递归必须留内存；`GetSubFoldersAsync`/`SortOrder max+1` 已 SQL 下推——**不纳入本方案**。

**验收**：OU 3 处下推 + DataDict 1 处下推落地；接口签名不变（消费方零迁移）；写路径（创建/移动/删除/字典维护）零触碰；既有测试断言全绿（锚点见 §六）；新增 VEntity 用例。

---

## 二、现状分析（精确到文件:行，bg_2927440c 探针确认）

### 2.1 OrganizationUnit —— 6 处 `GetAllAsync` 全量拉取

**物化路径字段**（`Entities/OrganizationUnitEntity.cs`）：`Level`（int，根=0）+ `Path`（string(1024)，`/A/B/C/` Code 拼接，MaxPathLength=1024）+ `Code` 白名单 `^[A-Za-z0-9_.-]+$`。

**`OrganizationUnitManager` 6 处全量调用**（`Managers/OrganizationUnitManager.cs`）：

| 位置 | 用途 | 可下推？ | 处置（oracle3 修订） |
|------|------|:---:|------|
| L83（Create SortOrder） | 同级排序 max+1 | 内存 LINQ | **不动**（写路径；L83-85 实为 `GetAllAsync` + 内存 `Max`——**非 SQL 下推**，oracle3 C-2/H2 更正标签） |
| L137（Delete 子节点计数） | 删除保护（**直接子节点**计数） | 可下推 | **`CountByParentIdAsync(id)`**（语义不变——直接子节点；**非 Path LIKE**——Path LIKE 会变成整棵子树计数，oracle3 C-3/H3 更正） |
| L167（Move 子树重算） | 移动 BFS 重算 Path/Level | **写路径**（需实体回写） | **不动**（VEntity 只读） |
| L240（GetTreeAsync） | 全树内存组装 | **留内存**（递归组装） | 不动 |
| L294（GetSubTreeAsync） | 子树（`Path.StartsWith(ou.Path)` 内存过滤） | ✅ **可下推**（Path 前缀） | `GetByPathPrefixAsync` 下推 |
| L312（GetAncestorsAsync） | 祖先链（Path 分段 + 内存 Code 匹配） | ✅ **可下推**（Code IN 分段） | `GetByCodesAsync` 下推 + **返回后校验 `Count == expected`，缺失段抛异常**（oracle3 C-6/M3——保持 `GetAncestors_MissingAncestor_Throws` L479 断言） |

**两步查询**（`OrganizationUnitUserEntityDataService.GetUserIdsByOrganizationUnitIdsAsync`）：`GetUserIdsInOrganizationUnitAsync` = 第一步 `GetSubTreeAsync(ouId)` 取子树 OU Id 集合 → 第二步 junction 查 UserId 去重——**可下推为 JOIN OU→OUUser 单查询**（`vw_UserOrganizationUnitView`）。**⚠️ 双模式（oracle3 C-5/M2）**：`includeDescendants=true` 走 VEntity（Path 前缀）；`includeDescendants=false` 走 `new List<long> { ouId }` → **保留现有 junction 单查**（不经 VEntity）——实施清单显式区分。

### 2.2 DataDictionary —— 两步查询 + 缓存 + 内存树形

**物化路径字段**（`Entities/DictionaryItemEntity.cs`）：`ParentCode`（string?）+ `Level`（int）+ `Path`（string(1024)）。

**`DictionaryManager.GetOrLoadAggregateAsync(code)`**（`Managers/DictionaryManager.cs` L188-211）：
- 缓存 key `DD:{Code}`（`IMemoryCache`，CacheExpirationSeconds=300），命中直接返回
- 未命中两步：① `DictionaryDefinitionEntityDataService.GetByCodeAsync(code)` ② `DictionaryItemEntityDataService.GetItemsByDefinitionIdAsync(defId)`（上限 1000）→ 内存 `BuildTree(items)` 递归组装（ParentCode 匹配）→ 写缓存
- 三处缓存失效反查（L213-261：`InvalidateCacheByDefinitionIdAsync`/`InvalidateCacheByItemIdAsync` 需反查 Code）

**可下推点**：① 步（定义查）+ ② 步（项查）→ **`vw_DictionaryItemView` JOIN Definition→Item 单查询**；**BuildTree 留内存**（递归组装）。

### 2.3 写路径划界（VEntity 只读不适用）

| 扩展 | 写路径 | 说明 |
|------|--------|------|
| OU | `CreateAsync`（L83 附近）/`MoveAsync`（L167）/`DeleteAsync`（L137） | 需实体回写（Path/Level/SortOrder 重算）——**不动**；L137 子节点计数可改 Path LIKE 下推（只读计数，不影响写语义） |
| DataDict | 定义/项 CRUD + 缓存失效 | 需实体回写 + 缓存维护——**不动** |

---

## 三、优化设计

### 3.1 `vw_UserOrganizationUnitView`（JOIN `OrganizationUnit` → `OrganizationUnitUser`）

- **JOIN 键**：`OrganizationUnitUser.OrganizationUnitId = OrganizationUnit.Id`（INNER，多对一，行数不变、`OrganizationUnitUser.Id` PK 透传唯一稳定——符合 AGENTS §8 VEntity 判据）。
- **建议投影列**（对齐 `GetUserIdsInOrganizationUnitAsync` 输出 + 子树过滤键）：

| # | 列 | 来源 | 说明 |
|---|---|---|---|
| 1 | `Id` | OUUser.Id | PK 透传（`IsPrimary=true`） |
| 2 | `OrganizationUnitId` | OUUser.OrganizationUnitId | |
| 3 | `UserId` | OUUser.UserId | 输出键（去重后） |
| 4 | `OUPath` | OU.Path | **子树下推键**（`WHERE OUPath LIKE '{ou.Path}%'`） |
| 5 | `OUCode` | OU.Code | 祖先分段匹配键 |
| 6 | `OULevel` | OU.Level | |
| 7 | `OUName` | OU.Name | 展示扩展 |

- **替代**：`GetUserIdsInOrganizationUnitAsync(ouId)`（`includeDescendants=true` 分支）→ 视图单查询（`WHERE OUPath LIKE '{ou.Path}%'`）→ 内存 `Distinct UserId`——**两步骤一**；`includeDescendants=false` 分支**保留现有 junction 单查**（oracle3 C-5/M2：`new List<long> { ouId }` → `GetUserIdsByOrganizationUnitIdsAsync`，不经 VEntity）。
- **⚠️ LIKE 通配符转义（oracle3 C-4/M1）**：OU Code 白名单 `^[A-Za-z0-9_.-]+$` **含下划线 `_`**（SQL LIKE 单字符通配符）——`Path LIKE '/A_B/%'` 会误匹配 `/AXB/`。**实施前必须实证** FreeSql `StartsWith` → `LIKE` 是否对参数 `_`/`%` 自动 ESCAPE；若否，改用 `LEFT(Path, @len) = @path` / `SUBSTRING(Path, 1, @len) = @path` 精确前缀比较（避免 LIKE 通配符歧义）。
- **型写范式**（对齐 Identity `UserRoleView.cs` 先例）：

```csharp
[Table(Name = "vw_UserOrganizationUnitView", DisableSyncStructure = true)]
[DomainGenerateCode(IsView = true,
    ViewSql = @"CREATE OR REPLACE VIEW ""vw_UserOrganizationUnitView"" AS
SELECT ouu.""Id"", ouu.""OrganizationUnitId"", ouu.""UserId"",
       ou.""Path"" AS ""OUPath"", ou.""Code"" AS ""OUCode"", ou.""Level"" AS ""OULevel"", ou.""Name"" AS ""OUName""
FROM ""OrganizationUnitUser"" ouu
INNER JOIN ""OrganizationUnit"" ou ON ouu.""OrganizationUnitId"" = ou.""Id""",
    ViewSqlSQLite = @"CREATE VIEW IF NOT EXISTS ""vw_UserOrganizationUnitView"" AS
SELECT ouu.""Id"", ...（同上，双引号不变）...",
    ExposeGraphqlQuery = false,   // ⚠️ 敏感（用户-OU 归属关系）——经门面暴露（C4 一致模式）
    DefaultPageSize = 50)]
public partial class UserOrganizationUnitView { }

partial class UserOrganizationUnitViewDataService(IDomainUser user, IEntityReadOnlyDAC<UserOrganizationUnitView> dac)
    : DomainReadOnlyDataServiceBase<UserOrganizationUnitView, UserOrganizationUnitViewDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>按子树 Path 前缀取用户-OU 归属（含后代）——替代两步查询。</summary>
    public async Task<List<UserOrganizationUnitView>> GetByOuPathPrefixAsync(string pathPrefix, CancellationToken ct = default)
        => await SelectAsync(v => v, predicate: v => v.OUPath!.StartsWith(pathPrefix), limit: 10000, ct: ct);
}
```

### 3.2 OU `GetSubTreeAsync` / `GetAncestorsAsync` 下推（不新增 VEntity——直接 SQL 谓词）

> 两处均为**单表 OU 查询**（非跨表 JOIN），**无需 VEntity**——直接在现有 DataService 补 SQL 下推方法即可（`Path LIKE` / `Code IN`）。**F12 关系（oracle3 C-7/L1 更正）**：物化路径树形查询模板是教学系列 §九 **F12** 候选（非 F7——F7 是 GroupByAsync 聚合下推）——本方案手写实现合理（F12 状态 🟡 观察无承诺时间线），但 **ADR 应标注"F12 落地后可回收手写实现"**，避免永久双轨。

- **`GetSubTreeAsync(ouId)`**：`OrganizationUnitEntityDataService` 新增 `GetByPathPrefixAsync(string pathPrefix, ct)`（`WHERE Path LIKE '{prefix}%'`，**转义见 §3.1 注记**）——替代 `GetAllAsync` + 内存过滤。
- **`GetAncestorsAsync(ouId)`**：Path 分段（`/A/B/C/` → A,B,C）→ 新增 `GetByCodesAsync(IReadOnlyList<string> codes, ct)`（`WHERE Code IN (...)`)——替代 `GetAllAsync` + 内存匹配。**⚠️ 返回后校验（oracle3 C-6/M3）**：Manager 必须校验 `returned.Count == segments.Length`，**对缺失段抛 `InvalidOperationException`**（保持 `GetAncestors_MissingAncestor_Throws` L479 断言）——设计显式包含此校验步骤，防止实施者遗漏。

### 3.3 `vw_DictionaryItemView`（JOIN `DictionaryItem` → `DictionaryDefinition`）

- **JOIN 键**：`DictionaryItem.DefinitionId = DictionaryDefinition.Id`（INNER，多对一，`DictionaryItem.Id` PK 透传）。
- **⚠️ "定义存在但无项"语义（oracle3 C-1/H1 关键修正）**：现有 `GetOrLoadAggregateAsync`（L195-199）语义 = 定义存在 → 返回 `DictionaryDefinitionWithItems(definition, items)`，**即使 items 为空也返回非 null 聚合**。INNER JOIN 在"定义存在但无项"时返回**零行**——Manager 会误判"定义不存在"返回 null，**破坏 `DictionaryTreeTests.cs` L191 `GetItemsTree_EmptyDictionary_ReturnsEmpty` 断言**。
- **裁定（采纳 oracle3 推荐方案 b）**：**保留 INNER JOIN**，Manager 缓存未命中路径改为：
  1. 先单查 `GetDefinitionByCodeAsync(code)`——**定义不存在返回 null**（语义保持）
  2. 定义存在 → 走 `vw_DictionaryItemView` 查项（**零行 = 空项列表**，非"定义不存在"）
  3. 常见场景仍两步合一（定义查 + 项查合一为视图单查仅缺空项判断），**空项定义低频**（需 2 次查询，可接受）且**不破坏 PK 透传不变量**。
- **建议投影列**（对齐 `GetOrLoadAggregateAsync` 聚合面 + 定义字段）：

| # | 列 | 来源 | 说明 |
|---|---|---|---|
| 1 | `Id` | Item.Id | PK 透传（`IsPrimary=true`） |
| 2 | `DefinitionId` | Item.DefinitionId | |
| 3 | `DefinitionCode` | **Definition.Code** | **核心收益**——外层过滤键（替代两步查定义） |
| 4 | `Code` | Item.Code | |
| 5 | `DisplayName` | Item.DisplayName | |
| 6 | `Value` | Item.Value | |
| 7 | `Order` | Item.Order | 排序键 |
| 8 | `IsEnabled` | Item.IsEnabled | |
| 9 | `ParentCode` | Item.ParentCode | BuildTree 键（留内存） |
| 10 | `Level` | Item.Level | |
| 11 | `Path` | Item.Path | 物化路径（备用） |
| 12 | `DefinitionDisplayName` | **Definition.DisplayName** | 展示扩展 |

- **替代**：`GetOrLoadAggregateAsync(code)` 未命中路径 → 视图单查询（`WHERE DefinitionCode == code`）→ 内存 `BuildTree`（ParentCode 递归组装）→ 写缓存——**两步骤一**。
- **缓存权衡**：VEntity 化后缓存**仍有效**（缓存聚合结果，非查询）——`DD:{Code}` 命中直接返回不触 DB；未命中走视图单查询。
- **⚠️ 与方案 2 C4 一致**：`ExposeGraphqlQuery = false`（字典数据非敏感但**经门面聚合语义**——BuildTree 后输出树形，裸视图输出平表，直连 GraphQL 绕过树语义）。

---

## 四、裁定点（需评审确认）

| # | 裁定点 | 建议 | 理由 |
|---|--------|------|------|
| **C1** | OU `GetSubTreeAsync`/`GetAncestorsAsync` 下推形态：VEntity vs 直接 SQL 谓词？ | **直接 DataService SQL 下推（不新增 VEntity）** | 两处均为**单表 OU 查询**（非跨表 JOIN）——VEntity 价值在跨表读模型联邦；单表 Path LIKE/Code IN 用 DataService 谓词即可，**无 VEntity 必要性**（"无必要勿增 SG"判据） |
| **C2** | `GetUserIdsInOrganizationUnitAsync` 两步查询下推：新增 `vw_UserOrganizationUnitView`？ | **新增 VEntity（README 已规划）** | 跨表 JOIN（OU→OUUser）——VEntity 读模型联邦价值成立 + README V0.2.0 已规划 + 顺带携带 OU 列（Path/Code/Level/Name）供未来扩展 |
| **C3** | DataDictionary `GetOrLoadAggregateAsync` 两步下推：新增 `vw_DictionaryItemView`？ | **新增 VEntity** | 跨表 JOIN（Definition→Item）——读模型联邦 + `DefinitionCode` 直接过滤键（替代两步查定义）；BuildTree 留内存（递归不可下推） |
| **C4** | 两视图 `ExposeGraphqlQuery` 策略？ | **显式 `false`（经门面）** | 对齐方案 2 C4 + UserCenter C3 方案 B——`vw_UserOrganizationUnitView` 含**用户-OU 归属关系**（私密）、`vw_DictionaryItemView` 裸平表绕过 BuildTree 树语义；数据访问统一经门面（`OrganizationUnitManager`/`DictionaryManager` 聚合语义）。**⚠️ 纪律偏离显式声明（oracle3 C-9/L3）**：本裁定**表面偏离 §4.2"直接暴露"纪律**——实际合理（敏感归属 + 树语义保护，对齐 F11 默认收敛），此处显式记录而非隐含；**⚠️ REST 无暴露结果（oracle3 C-8/L2）**：两视图因 `ExposeGraphqlQuery=false` + VEntity DataService 命名门控（isDataService early-return 不支持 `[GenerateController(FromDataService=true)]`）——**无任何 REST/GraphQL 直暴露 API**，仅经 Manager 门面消费（C4 策略一致，显式说明防实施者事后发现） |
| **C5** | FileManagement 是否纳入本方案？ | **排除** | 探针确认：`GetFolderTreeAsync` 全树递归必须留内存；`GetSubFoldersAsync`/`SortOrder max+1` 已 SQL 下推——**无优化空间** |

---

## 五、影响面与划界

| 面 | 变更 | 说明 |
|----|------|------|
| OU 读路径 | `GetSubTreeAsync`/`GetAncestorsAsync`/`GetUserIdsInOrganizationUnitAsync` 下推 | 接口签名不变（消费方零迁移）；内部实现改 SQL 谓词/视图；`GetUserIdsInOrganizationUnitAsync` **双模式**（`includeDescendants=true` 走 VEntity Path 前缀 / `false` 保留 junction 单查——oracle3 C-5/M2） |
| OU 写路径 | **零触碰** | `CreateAsync`（L83 内存 LINQ 非 SQL）/`MoveAsync`（L167）/`DeleteAsync`（L137）需实体回写（Path/Level/SortOrder 重算）；`DeleteAsync` 子节点计数**本期不改**（保持 `GetAllAsync` 计数——`CountByParentIdAsync` 下推列入实施清单但语义确认后再改，oracle3 C-3/H3 + C-10/L4） |
| DataDict 读路径 | `GetOrLoadAggregateAsync` 两步 → 视图单查询 + BuildTree | 接口签名不变；缓存 key `DD:{Code}` 不变（缓存聚合结果）；**"定义存在无项"语义保留**（先单查定义 + 视图查项零行=空项，oracle3 C-1/H1） |
| DataDict 写路径 | **零触碰** | 定义/项 CRUD + 缓存失效（反查 Code）不变 |
| FileManagement | **排除** | 无优化空间（探针确认） |
| 视图暴露 | **零 REST/GraphQL 直暴露** | 两视图 `ExposeGraphqlQuery=false` + VEntity DataService 命名门控——仅经 Manager 门面消费（oracle3 C-8/L2） |
| 生产部署 | 新增文档章节 | 两视图 ViewSql（PG + SQLite 双方言）写入使用指南；**生产需 DBA 手动执行 ViewSql** |

---

## 六、测试锚点与新增用例

### 既有断言须保持全绿（接口签名不变 → 天然保留）

- **OU**：`OrganizationUnitManagerTests.cs` D1-D10（路径/子树/祖先/移动/删除/白名单/长度守卫/事务原子性）+ D11（GetTree 全树）+ D12-D14（`GetUserIdsInOrganizationUnitAsync` 两步查询，`OrganizationUnitUserTests.cs`）+ D15-D17（孤儿防御——`GetTree_OrphanNode_Throws` L459 / `GetAncestors_MissingAncestor_Throws` L479，异常路径保留）。
- **DataDict**：`DictionaryTreeTests.cs`（三级层级/NullParentCode 归根/多根/禁用项/空字典）+ `DictionaryManagerCacheTests.cs`（缓存命中第二次返回）。
- **FileManagement**：不涉及（排除）。

### 新增用例

| # | 用例 | 验证点 |
|---|------|--------|
| N1 | OU `GetByPathPrefixAsync`：子树 Path 前缀查询下推 | SQL 谓词 + 含后代 |
| N2 | OU `GetByCodesAsync`：祖先链 Code IN 查询下推 + 缺失异常 | SQL 谓词 + 异常路径保留 |
| N3 | `vw_UserOrganizationUnitView`：JOIN 查询按 OUPath 前缀返回用户归属 | 读模型联邦 |
| N4 | `vw_DictionaryItemView`：JOIN 查询按 DefinitionCode 返回项（含定义字段） | 两步骤一 |
| N5 | `GetOrLoadAggregateAsync` 缓存路径回归：视图查询 → BuildTree → 写缓存 → 二次命中 | 缓存 + VEntity 协作 |
| N6 | 写路径回归：OU Move/Delete + DataDict CRUD 后缓存失效正确 | 零触碰验证 |

---

## 七、实施清单

1. **OrganizationUnit**：
   - 新增 `UserOrganizationUnitView.cs` + `DataServices/UserOrganizationUnitViewDataService.cs`（手写只读 DataService，`ExposeGraphqlQuery=false`——C4）
   - `OrganizationUnitEntityDataService` 新增 `GetByPathPrefixAsync`/`GetByCodesAsync`（单表 SQL 下推——C1；**LIKE 转义实证见 §九 P3**）
   - `OrganizationUnitManager` 三方法改走下推路径（接口签名不变）——`GetAncestorsAsync` **返回后校验 `Count == expected` 缺失抛异常**（oracle3 C-6/M3）；`GetUserIdsInOrganizationUnitAsync` **双模式**：`includeDescendants=true` 走视图 Path 前缀 / `false` 保留 junction 单查（oracle3 C-5/M2）
   - `DeleteAsync` 子节点计数：**本期保持 `GetAllAsync` 计数**（写语义不动）；`CountByParentIdAsync(id)` 下推**列入实施清单但语义确认后再改**（oracle3 C-3/H3 + C-10/L4——Path LIKE 会变整棵子树计数，弃用）
   - `OrganizationUnitUserEntityDataService.GetUserIdsByOrganizationUnitIdsAsync` 保留（`includeDescendants=false` + 其他消费方）
   - ✅ `.xCodeGen/extensions/organizationunit.xCodeGen.json` **已存在**（Scope="Entity" 全量扫描）——无需新建，重跑生成
   - **ADR 注记（oracle3 C-7/L1）**：手写 `GetByPathPrefixAsync`/`GetByCodesAsync` 在框架 **F12**（物化路径树形查询模板）落地后可回收——避免永久双轨
2. **DataDictionary**：
   - 新增 `DictionaryItemView.cs` + `DataServices/DictionaryItemViewDataService.cs`（`ExposeGraphqlQuery=false`——C4）
   - `DictionaryManager.GetOrLoadAggregateAsync` 未命中路径改：**先单查定义（不存在 → null）+ 定义存在走视图查项（零行 = 空项列表）**——"定义存在无项"语义保留（oracle3 C-1/H1 方案 b）+ BuildTree 留内存（缓存不变）
   - ✅ `.xCodeGen/extensions/datadictionary.xCodeGen.json` **已存在**——无需新建，重跑生成
3. **回归**：`dotnet build` slnx 0 错误 + 全量测试绿（29 项目 1394 用例基线 + 新增）
4. **文档**：两扩展使用指南补"VEntity 化/下推"章节（ViewSql 生产 DBA 要求）+ README 版本演进
5. **落档**：教学系列 §八 路线图更新（OU/DataDict 🟡 待评估 → ✅ 方案已评审；FileManagement 🔴 → ⚪ 排除）+ 本篇案例编号 04

---

## 八、验收标准

- [ ] `GetSubTreeAsync`/`GetAncestorsAsync` 单表 SQL 下推（无 `GetAllAsync` 全量内存过滤）——`GetAncestorsAsync` **返回后校验缺失段抛异常**（L479 断言保持）
- [ ] `GetUserIdsInOrganizationUnitAsync` **双模式**：`includeDescendants=true` 视图单查询 / `false` junction 单查（无全量拉取）
- [ ] `GetOrLoadAggregateAsync` 未命中路径：定义单查（不存在→null）+ 视图查项（零行=空项）——**`GetItemsTree_EmptyDictionary_ReturnsEmpty` 断言保持**（oracle3 C-1/H1）
- [ ] 写路径零触碰（OU Move/Delete/Create + DataDict CRUD + 缓存失效 diff 为空——L83/L137 本期不改）
- [ ] FileManagement 未触碰（排除——C5）
- [ ] 既有断言全绿 + 新增 N1-N6 用例绿
- [ ] `lsp_diagnostics` 变更文件干净
- [ ] 使用指南补 VEntity/下推章节 + 生产 DBA 建视图要求
- [ ] 两视图无 REST/GraphQL 直暴露（仅经 Manager 门面——C4/C-8）

---

## 九、实施前补全项

| # | 项 | 说明 |
|---|----|------|
| P1 | OU `DeleteAsync` 子节点计数语义 | **本期不改**（保持 `GetAllAsync` 计数——删除保护 = 直接子节点）；`CountByParentIdAsync(id)` 下推列入实施清单但**语义确认后再改**（oracle3 C-3/H3——Path LIKE 会变整棵子树计数，弃用） |
| P2 | `GetAncestorsAsync` Path 分段边界 | Path 格式 `/A/B/C/`——分段后 `Code IN (A,B,C)`；**根节点**（Path 单段）边界 + **返回后校验 `Count == expected` 缺失抛异常**（L479 断言保持，oracle3 C-6/M3） |
| P3 | `vw_UserOrganizationUnitView` 视图行 `OUPath.StartsWith` 的 SQL 翻译 | **实证验证 FreeSql `StartsWith` → `LIKE` 是否对 `_`/`%` 自动 ESCAPE**（OU Code 白名单含 `_`——LIKE 单字符通配符，oracle3 C-4/M1）；若否，改用 `LEFT(Path, @len) = @path` / `SUBSTRING(Path, 1, @len) = @path` 精确前缀比较；SQLite/PG 双方言一致性 |
| P4 | DataDict 缓存与视图一致性 | 视图查询结果 → BuildTree → 写缓存——**视图列变动不影响缓存 key**（缓存的是聚合 DTO 非视图行）；三处缓存失效反查路径不变；**"定义存在无项"缓存**（聚合含空项列表——视图零行≠定义不存在） |
| P5 | 测试宿主 ProjectMetaContext 核对 | 重跑 `run-xcodegen.ps1` 后核对 OU/DataDict 测试宿主 `OnRegisterInfrastructureServices` 返回 SG1 生成的 `ProjectMetaContext`（AGENTS §8 自检清单 #6）覆盖新 VEntity 自动注册 |
| P6 | F12 回收注记 | ADR 标注"框架 F12（物化路径树形查询模板）落地后可回收手写 `GetByPathPrefixAsync`/`GetByCodesAsync`"（oracle3 C-7/L1） |

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-09-30 | v0.1.0-draft | 初始草案——基于 bg_2927440c（OU+DataDict+File 内存拼装探针）精确结论：OU 3 处可下推/1 处留内存、DataDict 1 处可下推/1 处留内存、FileManagement 排除 |
| 2026-09-30 | v0.1.1 | **oracle3 评审 PASS WITH CONDITIONS（`bg_1570736f`）10 条件全修订**——C-1/H1（DataDict INNER JOIN 破坏"定义存在无项"语义 → 方案 b 先单查定义 + 视图查项零行=空项）、C-2/H2（§二 L83 标签更正——实为内存 LINQ 非 SQL）、C-3/H3（L137 Path LIKE → CountByParentIdAsync 且本期不改）、C-4/M1（LIKE `_` 通配符转义实证 + LEFT/SUBSTRING 备选）、C-5/M2（includeDescendants 双模式）、C-6/M3（GetAncestors 返回后校验）、C-7/L1（F7→F12 更正 + 回收注记）、C-8/L2（REST 无暴露显式说明）、C-9/L3（纪律偏离声明）、C-10/L4（L137 范围归属统一） |

---

## 评审记录

| 日期 | 评审人 | 结论 | 修订 |
|------|--------|------|------|
| 2026-09-30 | oracle3 | **PASS WITH CONDITIONS**——C1-C5 裁定方向正确、单表/跨表判据一致可辩护；10 条件（H1/H2/H3/M1 + 6 中低）已修订；P1-P10 建议纳入 | 全部条件修订完成（§二/§三/§四/§五/§七/§八/§九 + 变更记录 v0.1.1） |
