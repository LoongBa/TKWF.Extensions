# 转告 · AuthSurface 批次查询效率复查——分页 SQL 下推与分组统计原语

> **转告方**：TKWF 扩展模块组
> **接收方**：框架组（复核 / 参考）
> **日期**：2026-11-17
> **状态**：✅ 扩展侧已实测修复（v0.3.1 候选）——本转告记录实证，供框架组复核原语边界

## 背景

AuthSurface V0.3.0（批次管理端 + 码侧契约，tag `AuthSurface/v0.3.0` + `AuthCenter/v0.14.1`）交付后复查，批次门面 `ICodeBatchService` 三处查询实现存在效率缺陷（N+1 统计 / 全量取内存分页）。属**性能级缺陷（非正确性缺陷）**——统计口径与语义已测试锁定（AuthSurface 50 用例全绿），本次只谈查询路径下推。

## 缺陷清单（V0.3.0 现状 → v0.3.1 修复）

| # | 位置 | V0.3.0 实现 | v0.3.1 修复（扩展侧） |
|---|------|------------|----------------------|
| D1 | `CodeBatchService.GetBatchesAsync`/`GetBatchAsync` | **N+1**：列表页内每批一次 `GetStatsByBatchIdAsync`（`EntitySelectAsync(limit:int.MaxValue)` 全量拉码行 ≤5000/批 + C# 分桶）→ 页 20 = 21 次查询 | **单查询**：DataService 新增 `GetStatsByBatchIdsAsync`——`GroupByAsync` 按 `(BatchId, Status, IsExpired 时间派生布尔)` 复合键 SQL GROUP BY 全下推（IN 谓词过滤页内批次 + `g.Key.BatchId` 标量排序），C# 侧聚合分桶 |
| D2 | `CodeBatchService.GetBatchCodesAsync` | **全量取 + 内存分页**：`EntitySelectAsync(skip:0, limit:int.MaxValue)` 拉该批全部码行 → 内存 `Skip/Take`；Total 亦来自全量 | **SQL 分页**：`EntitySelectAsync(skip:(p-1)*size, limit:size)`（基座本就 `q.Skip().Take()` 下推）+ 基类 `CountAsync(predicate)` SQL 计数 |
| D3 | `RedemptionCodeEntityDataService.CountActiveByBatchAsync` | 全量拉 + C# `.Count` | 基类 `CountAsync(predicate)` SQL COUNT 下推（返回 int 强转） |

## 框架能力实证（D1 边界——请求框架组复核的点）

统计口径含**时间派生 expired**（`Status==0 && ExpireAtUtc != null && ExpireAtUtc <= now`）——分组键需含**布尔派生表达式**：

```csharp
.GroupByAsync(
    e => new { e.BatchId, e.Status, IsExpired = e.Status == 0 && e.ExpireAtUtc != null && e.ExpireAtUtc <= now },
    g => g.Key.BatchId,          // ⚠️ 排序必须选键的标量成员——整键 g.Key 不可翻译（实测 NRE）
    descending: false, topN: null,
    g => new { BatchId = g.Key.BatchId, Status = g.Key.Status, IsExpired = g.Key.IsExpired, Count = g.Count() },
    ct);
```

**扩展侧实测结论（SQLite，新增测试 `GetBatches_Stats_SingleGroupBySql_NotNPlusOne` 断言）**：

1. ✅ **布尔派生复合键（含条件表达式）进入 GROUP BY 键可翻译**——`Status==0 && ExpireAtUtc<=now`（now 为 C# 捕获参数）SQL 化无异常，计数正确（51/51 全绿含不变式断言）。
2. ⚠️ **排序/投影整键陷阱**：`orderBy: g => g.Key`（匿名整体）抛 `NullReferenceException`（`SelectGroupingProvider.InternalOrderBy` 翻译失败）——**必须选键的标量成员**（`g.Key.BatchId`）。框架 `GroupByAsync` 的 XML 文档未标注此限制，建议补文档/守卫（对匿名键给可操作错误消息，对齐 `GroupByAsync_NonFreeSqlQueryable_Throws_WithActionableMessage` 先例）。
3. ⚠️ **键成员类型需匹配投影**：实体 `BatchId` 为 `long?` → 匿名键推 `long?`；投影 `g.Key.BatchId` 亦 `long?`，C# 侧 `?? 0` 兜底（IN 过滤后不可达）。复合键成员类型推断一致性需调用方自察。

**请框架组**：① 复核第 2 点（整键排序 NRE）——若可加守卫/明确文档则更好；② 第 1 点在 PostgreSQL 方言的翻译确认（扩展侧 SQLite 已实测，PG 由 FreeSql 方言层处理、`EntityUpdateWhereAsync` 同类谓词已生产实证参数化，风险低）；③ 评估是否补 `GroupByAsync` 匿名键排序守卫 + 正例测试（布尔派生复合键）到框架测试套件。

## 影响面与本仓约束

- 仅 `AuthSurface` 受影响（`get-batches` / `get-batch-codes` / 吊销联动——消费方 EduPlatform B1 管理端点即这两查询）。
- 红线约束：扩展不直接注入 `IFreeSql`/`IEntityDAC`——所有下推经 DataService 基座（`QueryForUser()`/生成 `Query` internal 转发）或框架原语，无越界；新增 `TKWF.Domain.FreeSql` csproj 引用（分组聚合扩展所在程序集，双模式条件化，同 Tagging 先例）。
- ⚠️ 本转告初稿原判「D1 依赖框架组复核后方可修复」——复查中确认扩展侧可自行实证（SQLite 测试宿主即验证场），已提前闭环；框架组复核退化为追加保障。

<!-- EOF -->