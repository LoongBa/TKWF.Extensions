# ADR-AuditLogging-统一审计读模型与分面读取

> **版本**：v0.1
> **状态**：提议（待 Oracle 评审）
> **关联**：`docs/AuditLogging/v0.7.0-审计契约收口与统一读模型-开发方案.md` §三；v0.6.0 三面分层方案；《设计原则手册》P2/P3/P8/P13；`设计原则/扩展模块.md`
> **日期**：2026-11-27

## 目的与目标

为审计读侧建立**单一骨架**：统一读契约（`IAuditQueryService` + `IAuditFacetReader` + `AuditRecordView`）覆盖调用审计/安全事件/业务审计三面，使 v0.7 的业务审计读面与 v0.8 的导出/水位都构建在同一读模型上，避免"三面各做一遍再重构"。写侧仍维持三面分立（每面单一事实源）。读者 3 句话内明白：统一的是**读契约与视图**，不是写表；跨面只做 `CorrelationId` 聚合，不做统一分页。

## 问题

### 问题现象

1. v0.6 三面各自查询（`IAuditLogQueryService`/`ISecurityLogQueryService`/`IBusinessAuditQueryService`），无统一读契约；消费方要"统一查询/关联"需自行拼装，口径易漂移（P2）。
2. 未来导出（G6）、水位增量（G8）、平台总览若各自实现，会重复读逻辑并再次分叉。
3. DMP 诉求含"统一查询门面"（跨调用/安全/业务三面按 CorrelationId 串联）。

### 触发场景

- 消费方按审计面查询/计数/取详情。
- 需要把同一请求链路的"调用 + 安全事件 + 业务事实"按 `CorrelationId` 聚合。
- v0.8 导出/水位需要统一的读入口。

### 现有方案的不足

- **三面各自查询**：无统一视图与门面，导出/水位会重复实现。
- **三表并成大表**：破坏 P2（一表一义）、保留/权限/形状差异被迫兼容，且跨库无法合并。
- **统一分页**：三表无统一排序键/索引，跨表合并分页成本高且语义不一致。

## 使用场景

| 场景 | 使用方式 |
|---|---|
| 按面查询 | `IAuditQueryService.QueryAsync(AuditFacet.Business, query)` |
| 详情 | `GetDetailAsync(facet, id)`（策略门控后由订阅方读取） |
| 跨面链路聚合 | `GetByCorrelationAsync(correlationId)`（三面各查后合并，小集合） |
| 水位增量 | `GetIncrementalAsync(facet, afterId, batch)`（Id 升序） |
| 导出（v0.8） | `IAuditExportService` 消费 `IAuditQueryService`（facet 定向 + 流式） |

**不适用边界**：**不支持跨面统一分页/统一排序**（无统一排序键）；不把三表物理合并；不为跨库聚合提供本地实现（v0.8 走逐面水位 + 平台编排）。

## 决策

1. **统一读契约、写表分立**：`IAuditQueryService`（门面）+ `IAuditFacetReader`（每面一实现，集合注册，ADR92 `TryAddEnumerableConstructible`）+ `AuditRecordView`（抽象基类 + 三子类型）。
2. **跨面边界**：仅 `GetByCorrelationAsync` 跨面聚合；**不提供跨面统一分页**。
3. **视图用子类型**（强类型、可演进），公共字段在基类，facet 差异在子类；`Attributes` 承载业务自定义维度。
4. **契约在 Abstractions**，实现留主包；消费方只引契约（L2/ADR48 D7）。
5. **门面按 `AuditFacet` 定向委托**，不做隐式跨面猜测。

## 备选方案

| 方案 | 放弃原因 |
|---|---|
| A. 维持三面各自查询 | 无统一视图；导出/水位重复实现必分叉（P2） |
| B. 三表物理合并为大表 | 破坏一表一义/保留/权限；跨库无法合并（P2/P15） |
| C. 统一跨面分页 | 无统一排序键/索引；合并分页成本高、语义不一致 |
| D. 统一门面隐式猜面 | 语义模糊；与 P5 显式优先冲突 |

## 后果

- **正面**：读侧单一骨架；业务审计读面、导出、水位复用同一契约；消费方只引 Abstractions。
- **负面/缓解**：新增 reader 抽象与契约（一次性成本）；跨面不分页需文档明示，跨面检索走逐面 + 平台编排；`AuditRecordView` 演进需保持向后兼容（加法）。

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-11-27 | 初稿——统一读模型 + 分面读取 + 跨面不分页边界；待 Oracle 评审 |
