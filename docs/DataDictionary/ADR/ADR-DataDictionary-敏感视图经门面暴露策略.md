# ADR-DataDictionary-敏感视图经门面暴露策略

## 状态

活跃

## 一、目的与目标

确立 DataDictionary 扩展 VEntity 读模型（`vw_DictionaryItemView`）的暴露策略：**视图（裸平表绕过 BuildTree 树语义）默认关闭 GraphQL 直连，数据访问统一经既有门面（`IDictionaryManager`）暴露**。读者应在 3 句话内明白：DataDictionary V0.2.0 引入的定义-项 JOIN 视图（JOIN `DictionaryItem` → `DictionaryDefinition`）输出**平表**，而扩展对外语义是**树形聚合**（`GetItemsTreeAsync` BuildTree 递归组装）——`ExposeGraphqlQuery = false` 显式关闭（对照 VEntity 默认 `true`）；消费方读取字典仍走 `IDictionaryManager`（门面内缓存拦截 + 树形语义）；"定义存在无项"语义经先单查定义 + 视图零行=空项保留（oracle3 C-1/H1 方案 b）。

## 二、问题

### 问题现象

- VEntity 默认 `ExposeGraphqlQuery = true`——`vw_DictionaryItemView` 输出**平表**（无嵌套），GraphQL 直连返回平表绕过 `BuildTree` 树形聚合语义（消费方期望 `GetItemsTreeAsync` 的嵌套树）。
- 聚合语义（`GetDefinitionWithItemsAsync` 缓存拦截 + 树形组装）在 `IDictionaryManager` 内——GraphQL 直连无此语义，且绕过缓存（高频字典读取会直接打 DB）。
- **INNER JOIN 空项语义陷阱（oracle3 C-1/H1）**："定义存在但无项"视图返回零行——若 Manager 以视图行数判定"定义存在"，会误判"定义不存在"返回 null，破坏空字典语义（`GetItemsTree_EmptyDictionary_ReturnsEmpty`）。

### 触发场景

- 消费方启用 DataDictionary 后，SG 自动发现 VEntity——不显式关闭则 GraphQL 默认暴露字典平表。
- 字典读取需求（`GetDefinitionWithItemsAsync`/`GetItemsAsync`/`GetItemsTreeAsync`）——本应经 `IDictionaryManager` 门面（缓存拦截 + 树形语义 + 空项语义）。

### 现有方案不足

- 依赖"开发记得关"不可靠（默认 true 即裸暴露）；对照先例：Notifications `UserNotificationView` 收件箱私密面 `ExposeGraphqlQuery=true` 实为 IDOR 分析前决策——本方案同 Approval/PrintTemplates ADR（收紧而非差异），Notifications 先例列入框架演进候选 F4（OwnerFilter）/F11（默认收敛）回溯审查清单。

## 三、使用场景

### 适用场景

- DataDictionary 扩展全部读路径（`IDictionaryManager` 门面）——按编码聚合/树形查询保持缓存拦截 + 树形语义。
- 未来若需 GraphQL 直读字典平表（如管理后台字典维护网格）——按语义评估（是否需要树形）后再开，或经 Service 包装类暴露树形聚合。
- 与 Approval/PrintTemplates ADR（敏感视图经门面）同模式——聚合语义读模型统一经门面。

### 不适用边界

- 非敏感读模型不必关闭（按 F11 评估统一策略）。
- VEntity 内部查询（`DictionaryItemViewDataService.GetByDefinitionCodeAsync` 供 Store/Manager 聚合委托）不受影响——关闭 GraphQL 只影响外部直连，不阻塞门面内部委托。

## 四、选项

### 选项 A（采纳）：`ExposeGraphqlQuery = false`（经门面聚合）

- 描述：`vw_DictionaryItemView` 声明 `ExposeGraphqlQuery = false`；`GetOrLoadAggregateAsync` 未命中路径 = 先单查定义（不存在→null）+ 定义存在走视图查项（零行=空项列表）→ BuildTree 留内存 → 写缓存；`DictionaryItemViewDataService` 不标 `[GenerateController(FromDataService=true)]`。
- 优点：裸平表不绕过 BuildTree 树语义；缓存拦截保持（高频读取不直接打 DB）；"定义存在无项"语义保留（oracle3 C-1/H1 方案 b）；两步骤一（JOIN 下推 DB 替代"查定义 + 按 DefinitionId 查项"）。
- 缺点：需 GraphQL 直读平表时需先语义评估再开（当前无此需求）。

### 选项 B：`ExposeGraphqlQuery = true`（默认直连）

- 描述：不显式关闭，SG 自动生成 resolver，外部可直接查字典平表。
- 缺点：**平表绕过树形聚合语义**（消费方拿到非嵌套数据）；绕过缓存（高频字典读取直打 DB）；不采纳。

### 选项 C：视图内嵌树形递归（不采纳）

- 描述：视图用递归 CTE 输出嵌套树。
- 缺点：递归 CTE 双方言（PG/SQLite）实现差异大 + BuildTree 内存递归已有成熟实现 + 缓存仍拦截——过度设计，无收益。

## 五、结论

采纳**选项 A**：`vw_DictionaryItemView` 声明 `ExposeGraphqlQuery = false`（C4 裁定），数据访问统一经 `IDictionaryManager` 门面。实施：`DictionaryItemView.cs` `[DomainGenerateCode(IsView=true, ..., ExposeGraphqlQuery=false)]`；`DictionaryItemViewDataService.GetByDefinitionCodeAsync`（`IEntityReadOnlyDAC` 红线合规）供 Store 委托；`DictionaryManager.GetOrLoadAggregateAsync` 未命中路径改造（先单查定义 + 视图查项零行=空项——**空字典语义保留**，oracle3 C-1/H1 方案 b）；视图行映射回 `DictionaryItemEntity`（接口签名不变，Identity"接口不变"先例）；BuildTree 留内存（缓存仍有效——缓存聚合结果非查询）；测试宿主建真实视图验证 JOIN 下推。关联：04 倒推优化方案（OU/DataDict 内存拼装优化）C-1/C-3/C-4 裁定；框架演进候选 F4（OwnerFilter）/F11（默认收敛）。

## 六、变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-10-01 | 初始——V0.2.0 VEntity 化实施落档（04 方案 C-1/C-3/C-4 裁定） |
