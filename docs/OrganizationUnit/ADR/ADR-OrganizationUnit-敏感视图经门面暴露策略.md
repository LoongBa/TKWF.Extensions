# ADR-OrganizationUnit-敏感视图经门面暴露策略

## 状态

活跃

## 一、目的与目标

确立 OrganizationUnit 扩展 VEntity 读模型（`vw_UserOrganizationUnitView`）的暴露策略：**敏感视图（含用户-OU 归属关系）默认关闭 GraphQL 直连，数据访问统一经既有门面（`IOrganizationUnitManager`）暴露**。读者应在 3 句话内明白：OrganizationUnit V0.2.0 引入的用户归属 JOIN 视图（JOIN `OrganizationUnitUser` → `OrganizationUnit`）含用户-OU 归属明细（私密关系），`ExposeGraphqlQuery = false` 显式关闭（对照 VEntity 默认 `true`）；消费方访问用户归属仍走 `IOrganizationUnitManager.GetUserIdsInOrganizationUnitAsync`（门面内已有引用守卫/双模式语义）；未来需 GraphQL 直读时按安全评估再开。

## 二、问题

### 问题现象

- VEntity 默认 `ExposeGraphqlQuery = true`（`QueryExposureDefaults.cs`）——`vw_UserOrganizationUnitView` 含 `UserId`（用户 Id）与 OU 归属关系，不显式关闭即裸暴露。
- 用户-OU 归属是**私密关系数据**（组织归属可推导组织架构/汇报线/权限范围）——GraphQL 直连绕过门面引用守卫（已删 OU 查询拒绝）与业务语义。
- `includeDescendants` 双模式语义（oracle3 C-5/M2）在 `IOrganizationUnitManager` 内（`true` 视图 Path 前缀 / `false` junction 单查）——GraphQL 直连无此语义。

### 触发场景

- 消费方启用 OrganizationUnit 扩展后，SG 自动发现 VEntity——不显式关闭则 GraphQL 默认暴露用户-OU 归属。
- 用户归属查询需求（`GetUserIdsInOrganizationUnitAsync` 含/不含子孙）——本应经 `IOrganizationUnitManager` 门面（引用守卫 + 双模式）。

### 现有方案不足

- 依赖"开发记得关"不可靠（默认 true 即裸暴露）；对照先例：Notifications `UserNotificationView` 收件箱私密面 `ExposeGraphqlQuery=true` 实为 IDOR 分析前决策——本方案同 Approval/PrintTemplates ADR（收紧而非差异），Notifications 先例列入框架演进候选 F4（OwnerFilter）/F11（默认收敛）回溯审查清单。

## 三、使用场景

### 适用场景

- OrganizationUnit 扩展全部读路径（`IOrganizationUnitManager` 门面）——子树/祖先/用户归属查询保持门面语义。
- 未来若需 GraphQL 直读用户归属（如管理后台组织人员视图）——按 F4（"仅本人"过滤）安全评估后再开。
- 与 Approval/PrintTemplates ADR（敏感视图经门面）同模式——敏感读模型统一经门面。

### 不适用边界

- 非敏感读模型（无个人隐私/权限语义的聚合视图）不必关闭（按 F11 评估统一策略）。
- VEntity 内部查询（`UserOrganizationUnitViewDataService.GetByOuPathPrefixAsync` 供 Store/Manager 委托）不受影响——关闭 GraphQL 只影响外部直连，不阻塞门面内部委托。

## 四、选项

### 选项 A（采纳）：`ExposeGraphqlQuery = false`（敏感视图经门面）

- 描述：`vw_UserOrganizationUnitView` 声明 `ExposeGraphqlQuery = false`；`GetUserIdsInOrganizationUnitAsync(includeDescendants: true)` 经 `UserOrganizationUnitViewDataService` 委托（Store 中间层，Manager 门面消费）；`includeDescendants=false` 保留 junction 单查（双模式，oracle3 C-5/M2）。
- 优点：用户-OU 归属（私密关系）不经 GraphQL 裸暴露；消费方路径单一（门面）；单表下推（`GetSubTreeAsync`/`GetAncestorsAsync` 精确前缀/Code IN）不新增视图（"无必要勿增 SG"判据，oracle3 C-1）。
- 缺点：需 GraphQL 直读用户归属时需先安全评估再开（当前无此需求）。

### 选项 B：`ExposeGraphqlQuery = true`（默认直连）

- 描述：不显式关闭，SG 自动生成 GraphQL resolver，外部可直接查用户-OU 归属。
- 缺点：**用户-OU 归属私密关系裸暴露**（无"仅本人"过滤——OwnerFilter 未落地）；绕过门面引用守卫/双模式语义；不采纳。

### 选项 C：额外建脱敏视图（不采纳）

- 描述：另建无 UserId 的视图供 GraphQL。
- 缺点：视图链禁令（VIEW002，只允许引用基表）+ 双视图维护成本；当前无 GraphQL 需求，过度设计。

## 五、结论

采纳**选项 A**：`vw_UserOrganizationUnitView` 声明 `ExposeGraphqlQuery = false`（C4 裁定），数据访问统一经 `IOrganizationUnitManager` 门面。实施：`UserOrganizationUnitView.cs` `[DomainGenerateCode(IsView=true, ..., ExposeGraphqlQuery=false)]`；`UserOrganizationUnitViewDataService` 不标 `[GenerateController(FromDataService=true)]`（REST 经门面）；Store 中间层委托 + Manager 双模式消费；测试宿主建真实视图验证 JOIN 下推。**F12 回收注记（oracle3 C-7/L1）**：单表手写下推 `GetByPathPrefixAsync`/`GetByCodesAsync`（`OrganizationUnitEntityDataService`）在框架 **F12（物化路径树形查询模板）** 落地后可回收为模板生成——避免永久双轨，F12 落地时评估回收。关联：04 倒推优化方案（OU/DataDict 内存拼装优化）C-1/C-4/C-5 裁定；框架演进候选 F4（OwnerFilter）/F11（默认收敛）/F12（物化路径树形查询模板）。

## 六、变更记录

| 日期 | 变更内容 |
|------|---------|
| 2026-10-01 | 初始——V0.2.0 VEntity 化实施落档（04 方案 C-1/C-4/C-5 裁定 + F12 回收注记） |
