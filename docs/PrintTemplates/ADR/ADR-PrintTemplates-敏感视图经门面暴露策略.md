# ADR-PrintTemplates-敏感视图经门面暴露策略

## 状态

活跃

## 一、目的与目标

确立 PrintTemplates 扩展 VEntity 读模型（`vw_PrintTemplateVersionView`）的暴露策略：**敏感视图（含模板正文 Content 商业资产）默认关闭 GraphQL 直连，数据访问统一经既有门面（`ITemplateManager`）暴露**。读者应在 3 句话内明白：PrintTemplates V0.2.0 引入的模板-版本 JOIN 视图含模板正文（商业资产），`ExposeGraphqlQuery = false` 显式关闭（对照 VEntity 默认 `true`）；消费方读取模板/版本仍走 `ITemplateManager`（门面内已有版本生命周期语义）；读路径从 2 次 DB 往返降为 1 次（真消除往返）。

## 二、问题

### 问题现象

- VEntity 默认 `ExposeGraphqlQuery = true`——`vw_PrintTemplateVersionView` 含 `Content`（模板正文，商业资产——客户发票/单据格式）与模板键/描述，不显式关闭即裸暴露。
- 模板正文是**操作者可编辑的 Scriban 资产**（PrintTemplates V0.1.0 安全契约：非进程级沙箱，模板仅限受信后台编辑）——GraphQL 直连读取正文绕过门面/后台权限门控。
- 版本生命周期语义（Draft/Active/Archived 状态过滤 + `RenderAsync` 固定版本渲染）在 `ITemplateManager` 内——GraphQL 直连无此语义。

### 触发场景

- 消费方启用 PrintTemplates 后，SG 自动发现 VEntity——不显式关闭则 GraphQL 默认暴露模板正文。
- 渲染/查询需求（`RenderAsync`/`GetVersionAsync`/`GetActiveVersionAsync`/`ListVersionsAsync`）——本应经 `ITemplateManager`（生命周期语义 + 状态过滤）。

### 现有方案不足

- 依赖"开发记得关"不可靠（默认 true 即裸暴露）；对照先例：Notifications `UserNotificationView` 收件箱私密面 `ExposeGraphqlQuery=true` 实为 IDOR 分析前决策——本方案同 Approval ADR（收紧而非差异），Notifications 先例列入 F4/F11 回溯审查。

## 三、使用场景

### 适用场景

- PrintTemplates 全部读路径（`ITemplateManager` 门面）——版本查询/渲染保持门面语义。
- 未来若需 GraphQL 直读模板版本（如管理后台模板列表）——按安全评估后再开。
- 与 Approval ADR（敏感视图经门面）同模式——敏感读模型统一经门面。

### 不适用边界

- 非敏感读模型不必关闭（按 F11 评估统一策略）。
- VEntity 内部查询（`PrintTemplateVersionViewDataService` 供 `TemplateManager` 读路径委托）不受影响——关闭 GraphQL 只影响外部直连。

## 四、选项

### 选项 A（采纳）：`ExposeGraphqlQuery = false`（敏感视图经门面）

- 描述：`vw_PrintTemplateVersionView` 声明 `ExposeGraphqlQuery = false`；`ITemplateManager` 三读方法 + `RenderAsync` 经 `PrintTemplateVersionViewDataService` 委托；写路径（Publish/Draft/Archive）仍经 Store 实体读方法（VEntity 只读禁写，oracle3 C-high-2）。
- 优点：模板正文（商业资产）不经 GraphQL 裸暴露；读路径单查询 JOIN 下推（2 往返→1 往返）；消费方路径单一。
- 缺点：需 GraphQL 直读时先安全评估再开（当前无此需求）。

### 选项 B：`ExposeGraphqlQuery = true`（默认直连）

- 描述：不显式关闭，SG 自动生成 resolver，外部可直接查模板正文。
- 缺点：**模板正文商业资产裸暴露**（绕过受信后台编辑契约）；不采纳。

### 选项 C：额外建无正文视图（不采纳）

- 描述：另建无 Content 列的视图供 GraphQL。
- 缺点：视图链禁令（VIEW002）+ 双视图维护；`ListVersionsAsync` 投影排除 Content 已在方案 C-low-6 以指南注明处理（版本数大时考虑投影优化），无需建第二视图。

## 五、结论

采纳**选项 A**：`vw_PrintTemplateVersionView` 声明 `ExposeGraphqlQuery = false`（C4 裁定），数据访问统一经 `ITemplateManager` 门面。实施：`PrintTemplateVersionView.cs` `[DomainGenerateCode(IsView=true, ..., ExposeGraphqlQuery=false)]`；`ITemplateManager` 三读方法返回类型变更 `PrintTemplateVersionEntity` → `PrintTemplateVersionView`（C3，用户已裁定内部测试无历史负担、直接采用最优形态）；Store 实体读方法保留供写路径；测试宿主建真实视图验证（SQLite 方言 DateTimeOffset 列 NULL 占位——FreeSql SQLite provider 不支持 DateTimeOffset 建表，Identity 先例注释确认）。关联：02 倒推优化方案 C3/C4 裁定；框架演进候选 F4（OwnerFilter）/F11（默认收敛）。

## 六、变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-10-01 | 初始——V0.2.0 VEntity 化实施落档（02 方案 C3/C4 裁定） |
