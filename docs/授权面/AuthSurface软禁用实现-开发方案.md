# AuthSurface 软禁用语义实现 - 开发方案

> **立项档位**：C 档（仅方案文档，不写业务代码）
> **类型**：缺陷修复（文档承诺 vs 实现矛盾）
> **关联问题**：阶段性评审 AS-04
> **评审对象**：AuthSurface 扩展（AuthApp / 用户应用聚合）
> **目标版本**：v0.2.1（待定，见 §6）
> **日期**：2026-11-09

---

## 目录

1. [目标与背景](#1-目标与背景)
2. [文档承诺逐条引用](#2-文档承诺逐条引用)
3. [选项对比与方案选定](#3-选项对比与方案选定)
4. [实施细节](#4-实施细节)
5. [测试计划](#5-测试计划)
6. [文档同步与版本](#6-文档同步与版本)
7. [决策记录](#7-决策记录)
8. [变更记录](#8-变更记录)

---

## 1. 目标与背景

### 1.1 问题（AS-04）

**阶段性评审 AS-04（P1）**：AuthSurface 的 **IsEnabled 软禁用语义"承诺 vs 实现"矛盾**——两处代码文档承诺"IsEnabled=false → 应用不出现在'我的应用'聚合"，但三处实现环节均不兑现：

| 环节 | 现状（源码实证，2026-11-09） | 与承诺的关系 |
|------|------------------------------|-------------|
| `TKWFV_UserApps` 视图 | `UserAppsView.cs` 的 PG/SQLite 双 ViewSql **均不投影 `a."IsEnabled"`**（LEFT JOIN `TKWF_AuthApp` 仅取 `a."AppName"`） | 视图无该列 → 后续任何环节无法引用 |
| `GetActiveByUserIdAsync` predicate | `UserAppsViewDataService.cs` L23-24：仅 `UserId == userId && Status == 0 && (ValidUntil == null \|\| ValidUntil > UtcNow)` | **不含 IsEnabled 过滤**（且视图无列，客观上不可能含） |
| 门面 `GetAppsAsync` | `UserAppsQueryService.cs` L42-48：Select 映射后直接返回 | **无二次过滤**（无内存过滤兜底） |

**结论**：`SetEnabledAsync(appId, false)` 软禁用的应用**照常出现在"我的应用"聚合**（只要其 AuthGrant 授权仍 Active）——注释与实现矛盾。24 个测试用例（基线 23 + 976c5df AS-07 修复新增 `CreateWithOversizedPayload_Throws`）无软禁用排除断言（未覆盖该语义）。

### 1.2 语义矛盾的影响

- **行为**：应用被软禁用（运营标记下线）后，用户"我的应用"仍显示该应用并可用——运营下线意图落空。
- **当前实际杀伤**：受限——登入口在授权侧（AuthGrant Active 才有行），软禁用只影响运营展示意图；但 **AppName 敏感的消费方页面**会展示已下线应用，与"下线"运营预期不符。
- **文档一致性**：实体注释、方法 XML doc 承诺了门面保证语义——承诺未兑现 = 代码评审缺陷（AS-04 P1）。

### 1.3 目标

**兑现承诺语义**：`IsEnabled=false`（软禁用）→ 应用不出现于"我的应用"聚合（`GetAppsAsync` 结果）；`IsEnabled=true`/恢复 → 重新出现。**不改变** AuthGrant 授权数据本体（软禁用不吊销授权）。

### 1.4 范围边界

- **含**：视图列投影 + VEntity/DTO + DataService predicate + 测试 + 文档同步。
- **不含**：AuthGrant 吊销联动（软禁用仅聚合展示层语义，授权记录不动——README"软引用"语义保持）；兑换码 TargetAppId 指向（软禁用不影响兑换码有效性——防丢 AppName 语义保留）；`GET /grants`（AuthCenter 授权底座查询，非本扩展聚合）。

## 2. 文档承诺逐条引用

> 承诺来源共 **3 处**（实体注释、方法 XML doc、README）——指南 L136 是"防丢 AppName"建议（非聚合排除承诺，见 2.4 辨析）。

### 2.1 `AuthAppEntity.IsEnabled` 属性注释

| 项 | 内容 |
|----|------|
| 位置 | `_Framework/AuthSurface/Entities/AuthAppEntity.cs` L35-37 |
| 原文 | `/// <summary>启用标记——false = 软禁用（应用不出现在"我的应用"聚合结果语义由门面保证）。</summary>` |

承诺：**语义"由门面保证"**——即聚合查询门面负责实现"false → 不出现"。

### 2.2 `IAuthAppService.SetEnabledAsync` XML doc

| 项 | 内容 |
|----|------|
| 位置 | `_Framework/AuthSurface/Services/AuthAppService.cs` L33-34 |
| 原文 | `/// <summary>软禁用/启用（false = 应用不出现在"我的应用"聚合语义由目录状态承载——不物理删除）。</summary>` |

承诺：**"false = 应用不出现在'我的应用'聚合"**——直接承诺了聚合排除语义。

### 2.3 README（`_Framework/AuthSurface/README.md`）

| 项 | 内容 |
|----|------|
| 位置 | §"应用目录（"我的应用" AppName 来源）"、§"数据模型" |
| 原文 | `IAuthAppService.CreateAppAsync/UpdateAppAsync/SetEnabledAsync`——AppId 唯一目录（OAuth2 client 注册表语义）；软引用（无 FK——删除前检查引用，建议 `SetEnabledAsync(false)` 软禁用）|
| 数据模型 | `TKWF_AuthApp(... is_enabled BOOLEAN ...)`——列存在，语义表述为"软禁用" |

README 未像 2.1/2.2 那样字面承诺"聚合排除"，但与 2.1/2.2 同源（v0.1.0 设计意图：软禁用 = 运营下线）。

### 2.4 使用指南 L136（辨析：非聚合排除承诺）

| 项 | 内容 |
|----|------|
| 位置 | `docs/授权面/授权面扩展-使用指南.md` L136（§二-3"应用目录维护"末尾） |
| 原文 | `> ⚠️ **软删除建议**：AuthApp.AppId 与 AuthGrant.AppId/RedemptionCode.TargetAppId 为**字符串软引用**（无 FK 约束）——删除前需检查引用；建议 SetEnabledAsync(false) 软禁用而非物理删除（防 TKWFV_UserApps 丢 AppName / 兑换码 TargetAppId 指向已删应用）` |

**辨析**：L136 承诺的是**软禁用保留目录行**（防 LEFT JOIN 丢 AppName / 防兑换码指向已删应用）——即**软禁用的存在理由**，不直接承诺聚合排除。选择①后指南需**补一句**聚合排除说明（软禁用的展示层效果），选择②则 L136 语义不变。

### 2.5 矛盾点汇总

| 承诺（语义） | 实现（行为） | 期 |
|-------------|-------------|-----|
| false → 不出现在"我的应用" | 照常出现（视图无列 + predicate 无过滤 + 门面无兜底） | **矛盾（AS-04）** |
| 软禁用保留目录行（防丢 AppName） | ✅ 兑现（LEFT JOIN 保留行 + 目录行不删） | 一致 |

## 3. 选项对比与方案选定

### 3.1 选项 ① 实现承诺（视图投影 + predicate 过滤）

| 维度 | 内容 |
|------|------|
| 改动 | ① 双 ViewSql（PG+SQLite）SELECT 追加 `a."IsEnabled"`；② VEntity 追加 `IsEnabled` 列属性；③ xCodeGen 重生成（DTO .g.cs：属性/FromEntity/SelectExpression/ApplyToEntity）；④ DataService predicate 追加 IsEnabled 排除 && 加注释；⑤ 测试 ≥3 用例；⑥ 文档同步（指南补聚合排除说明 + 实体注释方法 doc 保留微调"已兑现"） |
| 成本 | **低**——字段与 SetEnabledAsync 方法已存在（v0.1.0），仅聚合语义缺失；无实体列变更（`TKWF_AuthApp` 表不动）；xCodeGen 重生成是既有管线（`.xCodeGen/extensions/authsurface.xCodeGen.json` 存在性需实施时核）；VEntity 变更含 .g.cs 重生成 = 常规实体型迭代开销 |
| DDL | **视图列变更**（`a."IsEnabled"` 投影）——`TKWFV_UserApps` 生产视图需 DBA 重建（CREATE OR REPLACE 幂等；开发环境 SyncViewsAsync 自动）；**基表不变**（`TKWF_AuthApp` 早已有 is_enabled 列） |
| 风险 | 见 3.3——LEFT JOIN NULL 语义（未录入目录的应用 IsEnabled 为 NULL，须视为"无目录状态不排除"）；视图重建窗口内聚合缺列 → 查询失败（短窗口，CREATE OR REPLACE 原子） |
| 支持证据 | 字段与方法已存在（`AuthAppEntity.IsEnabled` L35-37 + `SetEnabledAsync` L88-98）；聚合语义缺失是**实现欠账**而非设计质疑——评审 AS-04 推荐首选 |
| 兑现度 | **完全兑现** 2.1/2.2 承诺（2.3/2.4 一致） |

### 3.2 选项 ② 移除承诺（注释改为"运营标记不排除展示"）

| 维度 | 内容 |
|------|------|
| 改动 | 仅改 2.1 实体注释 + 2.2 方法 XML doc（+ README/指南措辞）；无代码行为变更、无 DDL、无重生成 |
| 成本 | **最低**（纯文档） |
| 风险 | **放宽安全/运营语义**——软禁用失去展示层效果，运营下线意图落空（消费方无其他线下手段——目录行是唯一 AppName 来源）；与 v0.1.0 设计意图（README"软禁用"、指南 L136 建议）**背道而驰**；`IsEnabled` 字段存在但语义残缺 = 半成品字段 |
| 适用 | 仅当视图投影变更风险高不可控（本方案 3.3 评估：风险可控）→ **不适用** |

### 3.3 关键风险评估（① 的唯一实质风险 = VEntity 投影变更）

| 风险 | 评估 |
|------|------|
| LEFT JOIN NULL 语义 | `a."IsEnabled"` 在**目录未录入应用**（LEFT JOIN 未命中）时为 SQL NULL——**NULL 视为"无目录状态不排除"**（与现状 AppName NULL 显示裸 AppId 同风格：目录未录入 ≠ 禁用）；predicate 用显式 **`(v.IsEnabled == null \|\| v.IsEnabled == true)`**（`== null` 翻译 IS NULL 有既有过滤先例背书，保留 NULL 行，见 4.4） |
| 列失配（现有 v0.2.0 包注记） | README 已有前例注记：`v0.2.0 包 ViewSql 含 ac."AuthLevel" 投影`与 AuthCenter V0.11.0+ 组合失配——本变更**仅加列不删列**，向后兼容现有视图，无同型失配 |
| DDL 变更 | 视图重建需 DBA（开发自动）——但**视图本身是跨扩展读模型**，生产本就依赖 DBA 执行 ViewSql（指南 §六）；变更先于版本发布、随包同步 ViewSql 文本，风险收敛在部署顺序（**必须先视图后 DLL**——建视图不依赖 DLL，但先升级 DLL 则新 DLL SELECT 含 `IsEnabled` 列而视图尚无 → 查询失败；见 4.6） |
| xCodeGen 重生成 | 标准管线（`run-xcodegen.ps1 -Ext AuthSurface`），.g.cs 入库；`UserAppsViewDto` 增列——**VEntity 内部 DTO**（DataService 经 `SelectExpression` 映射返回实体/DTO，门面组装对外 `UserAppDto` 不变）——**非消费方合约面增列**（`init` 属性，即便内部投影也非破坏） |

### 3.4 方案选定

> **选定：选项 ① 实现承诺**——字段、方法、设计意图俱在，唯缺聚合语义落地；成本低（视图加列 + predicate 过滤 + 常规重生成），"承诺 vs 实现"矛盾从根上消除；选项② 违背 v0.1.0 设计意图（README/指南"软禁用"语义）且使 `IsEnabled` 沦为半成品字段。评审修正计划 L295 在案（"视图加列 = DDL 变更（二选一）"）——本方案给出"选① + DDL 可控"的落地决策。

## 4. 实施细节

> 变更面 = **VEntity 投影链路**（视图 → VEntity 属性 → DTO → DataService predicate）+ 测试 + 文档；**基表/实体 0 变更**（`AuthAppEntity.IsEnabled` 列已存在）。

### 4.1 视图列投影（PG + SQLite 双 ViewSql）

`_Framework/AuthSurface/Views/UserAppsView.cs` `[DomainGenerateCode]` 两段 ViewSql 的 SELECT 列表各追加一列：

```sql
-- 变更点（PG 与 SQLite 同形，仅外层 CREATE 语句方言差异）
SELECT g."Id", g."UserId", g."AppId", a."AppName", g."Scopes",
       g."ValidUntil", g."Source", g."Status", g."CreateTime", ac."UId", a."IsEnabled"
FROM "TKWF_AuthGrant" g
INNER JOIN "TKWF_AuthAccount" ac ON g."UserId" = ac."UId"
LEFT JOIN "TKWF_AuthApp" a ON g."AppId" = a."AppId"
```

- **列序（评审闭环 P0——PG 硬约束）**：`a."IsEnabled"` **追加在 SELECT 末尾**（`ac."UId"` 之后）。PostgreSQL（9.4+）`CREATE OR REPLACE VIEW` **仅允许末尾追加列**——中间插列报 `cannot change name of view column`；生产已有 v0.2.0 视图时 DBA 执行新 ViewSql 若列在中间将直接失败。末尾追加 → 原位升级成功、零 DROP 窗口；与 VEntity `Position=11` 追加末尾对齐（4.2）。
- **LEFT JOIN NULL 语义**：目录未录入应用 → `IsEnabled` 为 SQL NULL（无目录状态）——语义定为**不排除**（与 AppName NULL 显示裸 AppId 同风格）；NULL 排除交由 C# 表达式处理（4.4）。
- **测试宿主同步（实施前置行）**：`AuthSurfaceTestHost.SyncSchema`（L56-78）硬编码一份 `TKWFV_UserApps` 的 SQLite ViewSql 副本（L71-77）——实施时**同步追加 `a."IsEnabled"` 列**（与 `UserAppsView.cs` ViewSqlSQLite 双源一致；宿主不跑 SyncViewsAsync，对齐 IdentityTestHost 先例——只改 UserAppsView.cs 不改宿主副本 → T1-T4 全红 `no such column`）。

### 4.2 VEntity 属性

`UserAppsView.cs` partial 类新增：

```csharp
/// <summary>应用目录启用标记（LEFT JOIN AuthApp——目录未录入时 null，视同启用不排除；false=软禁用聚合排除）。</summary>
[FreeSql.DataAnnotations.Column(Position = 11, IsNullable = true)]
public bool? IsEnabled { get; set; }
```

- 类型 **`bool?`（可空）**——非空 bool 读回 SQL NULL 会映射失真（目录未录入行语义错误）；可空保留"无目录状态"。
- **关键变更点（2026-11-09 实证）**：`UserAppsView.cs`（视图声明文件）**已存在 `Scopes` 且 Position=5**——本方案新增 `IsEnabled` 列插入 AppName 之后将**引发整列 Position 顺延**（Scopes→6、ValidUntil→7、Source→8、Status→9、CreateTime→10、UId→11）或**追加在 UId 之后（Position=11）**。
  - **决策：追加在末尾（Position=11）**——避免全列 Position 顺延造成 .g.cs 大范围 diff；**SQL SELECT 列序与 Position 对齐**（`a."IsEnabled"` 均追加末尾）——PG `CREATE OR REPLACE` 仅支持末尾追加列，SELECT 末尾既是 SQL 侧硬约束也是 Position=11 的自然对齐（FreeSql 按名映射，列序本不依赖，但此处天然一致）。
  - 同步更新类注释段落（⚠️ 视图不过滤 Status/ValidUntil……保留，追加"视图投影 IsEnabled 列——IsEnabled 过滤在 DataService（NULL=无目录状态不排除）"）。
  - **DTO 暴露面（评审闭环 P2）**：`IsEnabled` 是 DataService 内部过滤列——`UserAppsViewDto` 为 VEntity **内部 DTO**，门面组装对外 `UserAppDto` 不含本列（不上 wire，消费方合约零变化）；无需 `[JsonIgnore]`。敏感字段不外泄先例（AuthCenter `AppSecretEncrypted` = `DtoFieldIgnore` + `[JsonIgnore]`；AuthSurface `PayloadEncrypted` 不进读模型 DTO）用于实施时核对暴露面边界，非本列强制项。

### 4.3 xCodeGen 重生成

| 步骤 | 内容 |
|------|------|
| 配置核对 | `.xCodeGen/extensions/authsurface.xCodeGen.json` 存在（TargetProject → AuthSurface 主包；OutputDir 相对路径）——健康自检清单 #1/#2 |
| 执行 | `pwsh .xCodeGen/run-xcodegen.ps1 -Ext AuthSurface` → 重新生成 `Entities/UserAppsView.g.cs`（本文件仅模板骨架，属性在 hand-written .cs）+ `Entities/DTOs/UserAppsViewDto.g.cs`（属性 + `FromEntity` + `SelectExpression` + `ApplyToEntity` 增 `IsEnabled` 行）+ `Entities/DTOs/UserAppsViewDto.cs` 手写分部（无属性变更，核对注释） |
| 入库 | `.g.cs` 变更随下个提交（xCodeGen 生成物入库政策——禁止只改实体不提交生成物） |
| 核对 | 重新生成后 `git diff` 应仅含 `UserAppsView.cs`（+属性/注释/ViewSql）+ `UserAppsViewDto.g.cs`（+IsEnabled 四处）——无其他生成物漂移 |

### 4.4 DataService predicate 过滤（软禁用排除唯一落点）

`_Framework/AuthSurface/DataServices/UserAppsViewDataService.cs` `GetActiveByUserIdAsync`（L21-26）：

```csharp
/// <summary>取用户当前有效授权（Status=Active 且 ValidUntil 未过，且应用未软禁用——IsEnabled=false 排除；
/// NULL=目录未录入视同启用，按授权时间倒序）——仅本人。</summary>
public async Task<List<UserAppsView>> GetActiveByUserIdAsync(string userId, CancellationToken ct = default)
    => await SelectAsync(v => v,
        predicate: v => v.UserId == userId && v.Status == 0
            && (v.ValidUntil == null || v.ValidUntil > DateTime.UtcNow)
            && (v.IsEnabled == null || v.IsEnabled == true),   // 显式 NULL 展开——== null 翻译 IS NULL（与 ValidUntil==null 同型，先例背书）
        orderBy: q => q.OrderByDescending(v => v.CreateTime),
        limit: 200, ct: ct);
```

- **表达式语义**：`(v.IsEnabled == null || v.IsEnabled == true)`（可空 bool）——`== null` 显式展开翻译为 SQL `IsEnabled IS NULL`（与既有 `ValidUntil == null` 过滤**完全同型**，该翻译有仓库先例背书）、`== true` 翻译 `IsEnabled = TRUE`——NULL（目录未录入）与 TRUE 均保留、FALSE 排除。
- **首选写法（评审闭环 P1）**：**不采用** `v.IsEnabled != false`——可空 bool `!= false` 的 SQL 翻译**无仓库先例背书**（全仓 grep `IsEnabled ==/!=` 零匹配），FreeSql 大概率直译 `<> false` → NULL 行被误排除（与"NULL=不排除"语义恰好相反）；显式 NULL 展开消除方言差异风险（SQLite/PG 布尔字面量不同）。
- **实现期注意**：若 FreeSql 对 `== true` 的翻译在 SQLite/PG 存在布尔字面量差异（1/TRUE），退路为查询后 C# 内存过滤（`Where(x => x.IsEnabled is null or true)`）或 predicate 微调——T1-T4 断言锁定（§5）。

### 4.5 门面无二次过滤（确认）

`UserAppsQueryService.GetAppsAsync`（L37-49）——**保持现状，不添加二重过滤**：

- 理由：DataService predicate 已是单一过滤落点（视图保留全量、谓词下推 SQL——"有效过滤在 DataService"既有架构决策，UserAppsViewDataService.cs 类注释明示）；门面职责 = 映射 + DTO 组装，**双层过滤 = 重复实现 + 语义分裂风险**（Test 锁定单落点即可）。
- 只更新门面 XML doc（`<inheritdoc/>` 已继承 DataService 语义，若明确则补一句）：`GetAppsAsync` 返回"用户当前有效授权应用列表（Status=Active 且 ValidUntil 未过，**且目录未软禁用**，按授权时间倒序）"。

### 4.6 部署顺序（生产）

1. **先重建视图**：DBA 手动执行新版 ViewSql（`CREATE OR REPLACE` 原位升级——`a."IsEnabled"` 在 SELECT 末尾，PG 仅允许末尾追加列，升级零窗口；旧 DLL 查询老列集不失败，向后兼容）→ 2. 部署新扩展 DLL（predicate 生效）。
   **部署顺序硬约束（评审闭环 P0）**：**必须先视图后 DLL**——先 DLL 后视图：新 DLL 的 SELECT 含 `IsEnabled` 列而视图尚无该列 → 查询失败（`no such column`）。该顺序要求写入指南 §六部署要求（§3.3 DDL 变更行同步修正）。
2. 开发/测试环境：**无 DBA 动作**——测试宿主 `AuthSurfaceTestHost.SyncSchema` 以**硬编码视图 SQL 副本**建视图（**非 SyncViewsAsync**，对齐 IdentityTestHost 先例；实施须同步宿主 L71-77 加列，见 4.1/§5 前置注记）。

## 5. 测试计划

测试项目 `_Tests/Extension.AuthSurface.Tests`（现状 24 用例——基线 23 + 976c5df AS-07 修复新增 `CreateWithOversizedPayload_Throws`；新增用例归属"我的应用聚合"组）。**≥3 用例**（软禁用排除是 DataService predicate 行为——经生产路径 `GetAppsAsync` 门面断言，禁直测 DataService 绕门面）：

| # | 用例 | 断言 | 备注 |
|---|------|------|------|
| T1 | **软禁用后聚合排除**——建目录应用 A（AppName 录入）→ 造 login 源 AuthGrant（Active 有效）→ `SetEnabledAsync(A, false)` → `GetAppsAsync` | 结果**不含** A（AppId 缺失） | 核心回归——原 24 用例无此断言（AS-04 缺口） |
| T2 | **启用/恢复**——T1 基础上 `SetEnabledAsync(A, true)` → `GetAppsAsync` | 结果**重新包含** A（AppName 正确映射回显） | 语义闭环（1.3 目标） |
| T3 | **直查仍可见**——软禁用后 AuthApp 目录直查（`IAuthAppService`/目录 DataService 路径） | A **仍存在**且 `IsEnabled=false` | 软禁用 ≠ 物理删除（README 软引用语义锁定；防丢 AppName 意图保留） |
| T4（补充，审慎） | **LEFT JOIN NULL 语义**——未录入目录的应用 B（有 AuthGrant 无 AuthApp 行）`GetAppsAsync` | B **仍出现**（AppName null）且不被排除 | 锁定"目录未录入 ≠ 禁用"决策（3.3/4.2）——防 predicate 误伤 |

- 测试宿主：沿用既有双扩展白名单宿主（`[TKWFEnabledExtension(typeof(AuthSurfaceExtensionInitializer<>))]` + AuthCenter——视图 JOIN AuthGrant/AuthAccount 基表）；跨扩展基表由 AuthCenter 实体 SyncStructure 建（README 测试基线既有机制）。**⚠️ 前置步骤（评审闭环 P1）**：T1-T4 依赖 `AuthSurfaceTestHost.SyncSchema` L71-77 视图 SQL 已同步追加 `a."IsEnabled"` 列（实施清单见 §4.1 测试宿主行）——该行不落实则测试宿主视图缺列，T1-T4 全红（`no such column`）。
- 现有用例适配：**零破坏**（仅加列不改既有行为；T1-T4 新增，24 → 28 用例）。
- L1 受影响面：单扩展内部变更（无契约包/主框架依赖）→ 该扩展测试；slnx 构建 0 错误（L0 编译门）。

## 6. 文档同步与版本

### 6.1 文档同步

| 文档 | 变更 |
|------|------|
| `_Framework/AuthSurface/Views/UserAppsView.cs` | 4.1/4.2（ViewSql + 属性 + 类注释） |
| `_Framework/AuthSurface/DataServices/UserAppsViewDataService.cs` | 4.4（predicate + 注释） |
| `_Framework/AuthSurface/Services/UserAppsQueryService.cs` | 4.5（doc 微调，无代码逻辑变更） |
| `_Framework/AuthSurface/Services/AuthAppService.cs` + `Entities/AuthAppEntity.cs` | 注释微调：承诺**已兑现**——`SetEnabledAsync` XML doc 追加"（聚合排除语义：视图 IsEnabled 列 + DataService 过滤，v0.2.1 兑现）"；实体注释同型 |
| `_Framework/AuthSurface/README.md` | `IUserAppsQueryService.GetAppsAsync` 说明补"软禁用排除"；数据模型视图行补 `IsEnabled` 投影注记 |
| `docs/授权面/授权面扩展-使用指南.md` | L136 软删除建议后**追加一行**：软禁用效果 = 应用不出现在"我的应用"聚合（目录行保留、授权不吊销）；§六生产部署补"视图重建：新版本 ViewSql 含 `a."IsEnabled"` 投影——生产 DBA 先重建 `TKWFV_UserApps` 再升级 DLL（列只增不删，向后兼容）" |
| 本方案 → 评审流程 | C 档方案 → Oracle 评审（PASS WITH CONDITIONS 逐条闭环）→ 实施 |

### 6.2 版本

- **目标版本：v0.2.1**（patch——行为修复 + 目标语义补齐，无 API 破坏；v0.2.0 已发布为 2026-10-10 tag）。
- 版本机制：MinVer 前缀 tag `AuthSurface/v0.2.1`——**tag 必须征求用户同意**（§5 铁律）；实施完成 + 审核报告后打 tag + refs 刷新。
- CPM/框架依赖：零变化（不升框架版本、不加包）。

### 6.3 风险与回滚

| 风险 | 等级 | 缓解/回滚 |
|------|------|-----------|
| 视图列投影生产 DDL | 低 | `a."IsEnabled"` 追加在 SELECT 末尾（4.1）——PG `CREATE OR REPLACE` **原位升级零窗口**（仅末尾追加列合法；中间插列报 `cannot change name of view column`）；部署顺序固定"先视图后 DLL"（4.6）；**极端场景披露**：若需重排列序/列位调整则只能 DROP+CREATE——该路径有短窗口（DROP 后 CREATE 前视图缺列 → 聚合查询失败），写入指南 §六，非本方案默认路径 |
| 可空 bool SQL 翻译方言差异 | 低（首选即显式展开，已消除） | predicate 首选显式 `(v.IsEnabled == null \|\| v.IsEnabled == true)`——`== null` 翻译 IS NULL 有既有过滤先例背书（4.4 首选），**原 `!= false` 写法不采用**（无先例、`<> false` 直译误排除 NULL 行）；T1-T4 SQLite 真实视图断言锁定（PG 方言差异由 ViewSql 双版本同形 + 既有 Status/ValidUntil 过滤先例背书） |
| LEFT JOIN NULL 误排除（目录未录入应用消失） | 低 | T4 断言锁定；语义显式化（4.2 注释） |
| xCodeGen 重生成漂移 | 低 | 4.3 核对（git diff 仅限 2 文件 +） |
| **回滚方案** | — | ① **代码级（先）**：revert predicate + VEntity 属性/注释 + 测试宿主 L71-77 视图 SQL（三步一并回滚）——语义退回现状（软禁用无聚合效果），承诺注释同步退回或按②决策；**旧 DLL 兼容新旧视图**（列只增不删，新视图多列无害，可暂缓视图回滚）；② **视图级（后，可选）**：DBA 重建旧 ViewSql（CREATE OR REPLACE 旧 SELECT）——**非"两步独立可逆"**：代码回滚先行、视图回滚仅在需要清理列时执行；无数据迁移 |

## 7. 决策记录

| # | 决策 | 结论 | 理由 |
|---|------|------|------|
| D1 | 兑现还是移除承诺 | **实现**（选项①） | 字段（`IsEnabled` L35-37）、方法（`SetEnabledAsync` L88-98）、设计意图（v0.1.0 方案/README"软禁用"）俱在——仅聚合语义落地缺失；成本低且 AS-04 评审修正计划 L295 在案（视图加列二选一，本方案取实现侧） |
| D2 | 过滤落点 | **DataService predicate**（单一落点，门面不二次过滤） | 沿袭既有架构决策"有效过滤在 DataService"（UserAppsViewDataService 类注释）；双层过滤 = 重复实现 + 语义分裂风险 |
| D3 | 视图列 + VEntity 类型 | **追加列 + `bool?` 可空属性** | SQL LEFT JOIN NULL（目录未录入）须保留——非空 bool 读回 NULL 失真；Position 追加末尾（11）防 .g.cs 全列顺延大 diff |
| D4 | NULL 语义 | **目录未录入 = 视同启用（不排除）** | 与 AppName NULL 显示裸 AppId 同风格（无目录状态 ≠ 禁用）；T4 锁定 |
| D5 | 版本 | **v0.2.1（patch）** | 行为修复 + 语义补齐，无 API/契约破坏 |
| D6 | 部署顺序 | **先视图后 DLL** | `a."IsEnabled"` 列只增不删 + SELECT 末尾追加（PG `CREATE OR REPLACE` 原位升级允许）向前兼容旧 DLL；先 DLL 后视图则查询缺列失败（4.6） |

---

## 8. 变更记录

> 文档自身迭代记录（与产品版本无关——Agents §2 文档版本口径）。评审结论：oracle-pro4 **PASS WITH CONDITIONS（2026-11-09）**——条件逐条闭环修订进方案正文（P0/P1 必须项 + P2 要点），核心选型（方案① 视图投影 IsEnabled + DataService predicate 排除，NULL=目录未录入视同启用）与章节结构不变。

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-11-09 | — | **评审闭环 P0-1（视图列序）**：§4.1 SQL SELECT 将 `a."IsEnabled"` 移至**末尾**（`ac."UId"` 之后）——PG `CREATE OR REPLACE` 仅允许末尾追加列（中间插列 `cannot change name of view column`），生产 v0.2.0 视图原位升级零窗口；与 VEntity Position=11（§4.2）对齐 |
| 2026-11-09 | — | **评审闭环 P0-2（部署顺序）**：§3.3 DDL 变更行"先建视图或先升级 DLL 均可"矛盾表述统一为**必须先视图后 DLL**（先 DLL 后视图 → 新 DLL SELECT 含 IsEnabled 列而视图无 → 查询失败）；§4.6 同步澄清"两步互不依赖先后"矛盾表述 |
| 2026-11-09 | — | **评审闭环 P0-3（风险/回滚）**：§6.3 视图列投影 DDL 行更新——原位升级零窗口 + 极端 DROP+CREATE 路径窗口风险披露；回滚方案行更新——代码回滚先行（旧 DLL 兼容新旧视图）、视图回滚后置可选（非"两步独立可逆"） |
| 2026-11-09 | — | **评审闭环 P1-1（predicate 首选写法）**：§4.4 predicate 首选改显式 `(v.IsEnabled == null \|\| v.IsEnabled == true)`（`== null`→IS NULL 与既有 `ValidUntil == null` 同型有先例背书）；原 `!= false` 写法不采用（无仓库先例、`<> false` 直译误排除 NULL 行）；"SQL 翻译为 IS NULL OR = TRUE"断言注释修正为"显式 NULL 展开消除方言差异"；§3.3 NULL 语义行、§6.3 方言差异行（降级为"已消除"）同步 |
| 2026-11-09 | — | **评审闭环 P1-2（测试宿主）**：§4.6 测试环境机制描述修正——`AuthSurfaceTestHost.SyncSchema` 为**硬编码视图 SQL 副本**（非 SyncViewsAsync，对齐 IdentityTestHost 先例）；§4.1 实施清单加测试宿主行（L71-77 同步追加 `a."IsEnabled"` 列，双源一致）；§5 测试计划注明 T1-T4 前置步骤（宿主视图缺列即全红） |
| 2026-11-09 | — | **评审闭环 P2 要点**：测试基线 23→**24**（976c5df AS-07 新增 `CreateWithOversizedPayload_Throws`；新增 4 后 23→27 修正为 **24→28**——§1.1/§5 三处）；"消费方合约面增列"措辞收紧（`UserAppsViewDto` 为 VEntity 内部 DTO、门面组装对外 `UserAppDto` 不变——§3.3/§4.2）；回滚顺序澄清（先代码后视图，非两级独立可逆）；DTO 暴露面与敏感字段 `[JsonIgnore]` 先例对齐说明（AuthCenter `AppSecretEncrypted` DtoFieldIgnore+JsonIgnore 不外泄——§4.2 注记） |

---

## 附：支撑度判断

- **承诺证据**：3 处代码/文档承诺（§2.1 实体注释、§2.2 方法 XML doc、§2.3 README）——"软禁用不出现于聚合"语义**成立**。
- **实现证据**：3 处实现环节均无该语义（视图不投影 / predicate 不含 / 门面无兜底）——**矛盾实证成立**（AS-04 完整问题：评审记录 `..\..\_TKWF\docs\03_扩展模块\阶段性评审\扩展模块-阶段性评审记录-2026-11-08.md` L451；修正计划同目录 `-修正计划-2026-11-08.md` L295）。
- **支撑度结论**：**问题成立、修复方向单一（①）**——"承诺 vs 实现"矛盾即缺陷本体，无行为歧义；唯一方案分歧（① vs ②）由设计意图证据（README/指南"软禁用"建议 + 字段方法存在）裁定为 ①。
- **选定方案一句话**：**选项①——视图投影 `a."IsEnabled"` + DataService predicate 追加排除（NULL 视同启用），清理 AS-04 承诺与实现矛盾，v0.2.1 patch 发布**。