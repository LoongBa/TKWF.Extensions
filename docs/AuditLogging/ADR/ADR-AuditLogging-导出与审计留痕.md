# ADR-AuditLogging-导出与审计留痕

> **版本**：v0.1
> **状态**：提议（v0.7.0 合并迭代，待实施）
> **关联**：`docs/AuditLogging/v0.7.0-审计契约收口与统一读模型-开发方案.md` §11.2（G6 并入）；Oracle C5（keyset）；需求 DMP `审计能力装配分析-转扩展组.md` §8.2 BR-24；《设计原则手册》P3/P7/P14/P15；AGENTS §8（内容列红线）
> **日期**：2026-11-27

## 目的与目标

提供**受控审计导出**：消费方按面（业务审计优先）导出，行数硬上限 10000，导出动作本身留痕（谁、何时、条件摘要、条数），并发布可选 `AuditExportCompleted` 事件。目标是满足 DMP R05 BR-24，同时不破坏 `Take≤200` 的列表安全护栏（导出走独立流式/keyset 通道，而非放大列表页）。读者 3 句话内明白：导出不是"把 Take 调大"，而是独立服务 + keyset 游标 + 上限 + 留痕。

## 问题

### 问题现象

1. DMP R05 BR-24 要求导出 ≤10000 条；而 `search-logs` 等列表 `Take≤200`，消费方侧循环拉取既慢又易越权/漏审计。
2. 导出属敏感动作（可能含 Before/After/Attributes PII），若不留痕则无法追责。
3. 一次性全量加载大结果集有 OOM 风险。

### 触发场景

- 合规/管理端导出审计记录（CSV/Excel）。
- SIEM/平台总览批量拉取（走水位增量，非导出）。

### 现有方案的不足

- 放大 `Take`：破坏列表护栏、越权面扩大。
- 消费方自建导出：无统一上限/留痕/权限，口径漂移。
- 无上限/无游标：大结果集 OOM。

## 使用场景

| 场景 | 使用方式 |
|---|---|
| 业务审计导出 | `IAuditExportService.ExportAsync(facet, query, format, ct)`（keyset 游标内部循环，流式） |
| 格式 | CSV（零依赖）/ Excel（框架 MiniExcel formatter）；formatter 可插拔 |
| 留痕 | 导出前/后写 `BusinessAudit`（Action=`Exported`，Summary=条件摘要+条数，Actor=调用方） |
| 事件 | `AuditExportCompleted`（本地事件，默认关） |
| 权限 | `AuditPermissionNames.Export` + `IAuditAccessPolicy` |

**不适用边界**：跨库合并导出（走逐面 + 平台编排/Outbox）；替代查询/统计；无上限全量导出。

## 决策

1. **独立服务** `IAuditExportService`（`[ApiExpose]`，方法级可收窄）——不放大列表 `Take`。
2. **keyset 游标**：复用 `AuditFacetQuery.AfterId`（C5），按 Id 升序分批拉取 + 流式写出，避免 offset 深分页与全量加载。
3. **硬上限 10000**：超限返回结构化错误（P14），不是静默截断。
4. **导出留痕**：导出动作写 `BusinessAudit`（Action=`Exported`，条件摘要 + 条数 + Actor）；留痕失败按策略（默认不阻断导出但告警，与 G7 一致）。
5. **`AuditExportCompleted` 事件默认关**（E6）；载荷只带条件摘要/条数/操作者，无原文。
6. **formatter 可插拔**：CSV 零依赖内置；Excel 走框架 `TKWF.Utility.DataPort.Providers.MiniExcel`（已在 CPM，不新增第三方，P9）。

## 备选方案

| 方案 | 放弃原因 |
|---|---|
| A. 放大列表 Take 至 10000 | 破坏护栏/越权面/内存风险 |
| B. 消费方自建导出 | 无统一上限/留痕/权限，口径漂移 |
| C. 无上限流式 | 合规风险（导出滥用） |
| D. 导出不留痕 | 不可追溯（P7） |

## 后果

- **正面**：满足 BR-24；导出可追溯、受控、可插拔格式；与列表护栏解耦。
- **负面/缓解**：上限 10000 需文档（更大规模走水位/平台编排）；导出留痕写业务审计表会增长 → 与业务审计同保留策略。

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-11-27 | 初稿——受控导出 + keyset + 上限 + 留痕 + E6 事件；v0.7.0 合并迭代 |
