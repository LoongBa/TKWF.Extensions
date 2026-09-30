# ADR-Approval-敏感视图经门面暴露策略

## 状态

活跃

## 一、目的与目标

确立 Approval 扩展 VEntity 读模型（`vw_ApprovalTaskView`）的暴露策略：**敏感视图默认关闭 GraphQL 直连，数据访问统一经既有门面（`IApprovalQueryService`）鉴权暴露**。读者应在 3 句话内明白：Approval V0.3.0 引入的任务链 JOIN 视图含审批人明细/审批意见等敏感列，`ExposeGraphqlQuery = false` 显式关闭（对照 VEntity 默认 `true`）；消费方访问审批数据仍走门面（门面内已有状态机/权限语义）；未来需 GraphQL 直读时按安全评估再开。

## 二、问题

### 问题现象

- VEntity 默认 `ExposeGraphqlQuery = true`（`QueryExposureDefaults.cs`：`ResolveGraphql → IsView(entity)`）——新建视图不显式关闭即裸暴露。
- `vw_ApprovalTaskView` 投影列含 `ApproverUserId`（审批人 ID）/`ApprovedBy`（审批通过人）/`Comment`（审批意见）/`TransferredTo`（转交目标）等审批敏感明细——经 GraphQL 直连绕过业务鉴权。
- 审批语义含"仅本人/审批人可见"（`GetPendingTasksAsync` 按 `ApproverUserId` 过滤）与状态机约束（`Pending→Approved/Rejected`）——框架当前无"仅本人"过滤（`IGlobalQueryFilter` 仅 `TenantGlobalQueryFilter`，OwnerFilter 为框架演进候选 F4），GraphQL 直连无此保护。

### 触发场景

- 消费方启用 Approval 扩展后，SG1 自动发现 VEntity 并生成能力清单——若不显式关闭，GraphQL 端点默认暴露任务链（含审批人明细）。
- 审批详情/待办查询需求——本应经 `IApprovalQueryService.GetInstanceDetailAsync`/`GetPendingTasksAsync`（门面内鉴权 + 状态语义）。

### 现有方案不足

- 依赖"开发记得关"不可靠（默认 true 即裸暴露）；对照先例：Notifications `UserNotificationView`（收件箱私密面）`ExposeGraphqlQuery=true` 实为 UserCenter C3 IDOR 分析**前**的既有决策——本方案是对先例的**收紧而非差异**，Notifications 先例列入框架演进候选 F4（OwnerFilter）+ F11（默认收敛）回溯审查清单。

## 三、使用场景

### 适用场景

- Approval 扩展全部读路径（`IApprovalQueryService` 门面）——任务链/实例/待办查询保持门面鉴权。
- 未来若需 GraphQL 直读任务链（如管理后台跨实例审计视图）——按 F4 安全评估（"仅本人/审批人"过滤落地）后再开启。
- 与 UserCenter C3 方案 B（组合视图经门面暴露）同模式——敏感读模型统一经门面。

### 不适用边界

- 非敏感读模型（无个人隐私/权限语义的聚合视图）不必关闭——按 F11（默认收敛候选）评估统一策略。
- VEntity 内部查询（`ApprovalTaskViewDataService.GetTasksByInstanceIdAsync` 供 `GetInstanceDetailAsync` 内部调用）不受影响——关闭 GraphQL 只影响外部直连，不阻塞门面内部委托。

## 四、选项

### 选项 A（采纳）：`ExposeGraphqlQuery = false`（敏感视图经门面）

- 描述：`vw_ApprovalTaskView` 声明 `ExposeGraphqlQuery = false`；数据访问统一经 `IApprovalQueryService`（门面内已有状态机/权限语义）；`ApprovalTaskViewDataService` 仅作 QueryService 内部委托。
- 优点：审批敏感列（ApproverUserId/ApprovedBy/Comment）不经 GraphQL 裸暴露；消费方路径单一（门面）；与 UserCenter C3 方案 B 同模式。
- 缺点：需 GraphQL 直读审批数据时需先安全评估再开（当前无此需求）。

### 选项 B：`ExposeGraphqlQuery = true`（默认直连）

- 描述：不显式关闭，SG 自动生成 GraphQL resolver，外部可直接查任务链。
- 缺点：**审批敏感明细裸暴露**（无"仅本人/审批人"过滤——OwnerFilter 未落地）；绕过门面状态机/权限语义；不采纳。

### 选项 C：额外建脱敏视图（不采纳）

- 描述：另建无敏感列的视图供 GraphQL。
- 缺点：视图链禁令（VIEW002，只允许引用基表）+ 双视图维护成本；当前无 GraphQL 需求，过度设计。

## 五、结论

采纳**选项 A**：`vw_ApprovalTaskView` 声明 `ExposeGraphqlQuery = false`（C4 裁定），数据访问统一经 `IApprovalQueryService` 门面。实施：`ApprovalTaskView.cs` `[DomainGenerateCode(IsView=true, ..., ExposeGraphqlQuery=false)]`；`ApprovalTaskViewDataService` 不标 `[GenerateController(FromDataService=true)]`（REST 经门面）；测试宿主建真实视图验证 JOIN 下推。关联：02 倒推优化方案（Approval/PrintTemplates VEntity 化）C4 裁定；框架演进候选 F4（OwnerFilter）/F11（默认收敛）。

## 六、变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-10-01 | 初始——V0.3.0 VEntity 化实施落档（02 方案 C4 裁定） |
