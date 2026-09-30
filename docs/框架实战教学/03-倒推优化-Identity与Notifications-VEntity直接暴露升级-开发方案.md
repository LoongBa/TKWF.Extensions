# 03-倒推优化开发方案：Identity 与 Notifications VEntity 直接暴露升级

> **系列**：框架实战教学（开篇见 [`01-扩展模块最优解探索-Schema级数据组合-开篇.md`](./01-扩展模块最优解探索-Schema级数据组合-开篇.md)）
> **案例定位**：倒推路线图（§八）**保守形态 → 直接暴露**升级——Identity/Notifications 现有 VEntity「映射回原实体」策略升级为「VEntity DTO 一等公民」（§8.3 认知修正的落地）
> **涉及扩展**：`TKWF.Ext.Identity`（当前 V0.3.3）→ 目标 V0.4.0；`TKWF.Ext.Notifications`（当前 V0.4.1）→ 目标 V0.5.0
> **状态**：📋 待 Oracle 评审
> **版本**：v0.1.0-draft

---

## 一、目的与目标

对 Identity / Notifications 两个扩展的**既有 VEntity 读模型**执行「直接暴露」升级——遵循 2026-09-30 用户裁定"当前内部测试、无历史负担——直接采用最优形态，不留兼容双轨"：

1. **VEntity DTO 一等公民**：`UserRoleViewDto` / `UserNotificationViewDto`（SG1 已生成）直接作为 **REST API 返回类型**，替代"视图行映射回原实体"（`RoleEntity`/`UserNotificationEntity`）的保守形态——消费方经 REST 直接获得**视图完整字段**（含 VEntity JOIN 携带的跨表列：角色 DisplayName/IsSystemRole、通知 Name/Severity/DisplayName），不再丢弃。
2. **REST 直接暴露**：新增 **Service 包装类**（`UserRoleViewQueryService` / `UserNotificationViewQueryService`）——**机制事实（bg_1dc747d4 探针确认）**：VEntity 手写只读 DataService **不支持 `[GenerateController(FromDataService=true)]`**（`ControllerGenerator.cs` L629-642 `isDataService` 命名约定门控 early-return），REST 暴露唯一路径 = **非 DataService 命名的 Service 类** + `[GenerateController]`（public async 方法自动纳入契约）。
3. **GraphQL 已就绪零改动**：两 VEntity 均已标 `ExposeGraphqlQuery=true`，SG1b 自动 resolver 已就位（独立于 REST 门控扫描 L3070-3124）——本方案不涉及 GraphQL 改动。
4. **门面内部链路保留**：`IUserManager.GetUserRolesAsync` / `INotificationStore.GetListAsync` 内部仍调 VEntity DataService（返回实体——`IdentityRoleProvider`/`IdentityUserHelperBase` 业务消费需要 RoleEntity 的 Id/Name），**现有测试断言零破坏**；REST 新端点与门面并存（两种消费面：内部业务调用 vs 外部 API）。

**验收**：REST 新端点返回 VEntity DTO（视图完整字段）；GraphQL 不变；门面链路零改动；既有测试断言全绿（锚点见 §六）；新增 REST Service 用例。

---

## 二、现状分析（精确到文件:行，bg_1dc747d4 探针确认）

### 2.1 Identity —— `UserRoleView` VEntity + 映射回 RoleEntity

- **VEntity 实体**：`UserRoleView.cs`（列 `Id/UserId/Name/DisplayName/IsSystemRole/CreateTime/UpdateTime`；`[DomainGenerateCode(IsView=true, ExposeGraphqlQuery=true, DefaultPageSize=50)]`；PG+SQLite 双方言 ViewSql）
- **手写只读 DataService**：`DataServices/UserRoleViewDataService.cs`——`DomainReadOnlyDataServiceBase<UserRoleView, UserRoleViewDto>` + `IEntityReadOnlyDAC<UserRoleView>`，方法 `GetRolesByUserIdAsync(long userId, CancellationToken)`；**不标 `[GenerateController]`**（L17-18 注释："REST 经 IUserManager 门面暴露"）
- **消费链（映射回原实体——保守形态）**：
  - `UserStore.GetRolesAsync`（L86-104）→ `_userRoleViewDataService.GetRolesByUserIdAsync(userId)` → 视图行**映射回 `RoleEntity`**（Id/Name/DisplayName/IsSystemRole/CreateTime/UpdateTime 六字段）；异常静默 LogWarning 返回 `Array.Empty<RoleEntity>()`
  - `UserManager.GetUserRolesAsync`（L113-114）→ `_userStore.GetRolesAsync(userId)` 直委托
  - `IUserManager.GetUserRolesAsync`（L38 签名）→ `IReadOnlyList<RoleEntity>`
  - `IdentityRoleProvider<TUserInfo>`（V0.3.0）→ `IUserManager.GetUserRolesAsync` + **Scoped 缓存**（`Dictionary<string, IReadOnlyList<string>>?` userId→roles，消除 PermissionChecker 2N 放大；`AddScoped` 覆盖默认）
  - `IdentityUserHelperBase<TUserInfo>`（L37）→ `userManager.GetUserRolesAsync`（登录衔接填充 Roles）
- **DTO 已生成**：`UserRoleViewDto`（`.g.cs` L23-41——Id/UserId/Name/DisplayName/IsSystemRole/CreateTime/UpdateTime 七字段，`IDomainDto<UserRoleView>`，`IsFromPersistentSource`）+ `.biz.cs` 骨架（`partial record` + `OnCustomValidate` 空实现）

### 2.2 Notifications —— `UserNotificationView` VEntity + 映射回 UserNotificationEntity

- **VEntity 实体**：`UserNotificationView.cs`（列 `Id/UserId/NotificationId/State/ReadTime/CreateTime/Name/Severity/DisplayName`；`ExposeGraphqlQuery=true`）
- **手写只读 DataService**：`DataServices/UserNotificationViewDataService.cs`——方法 `GetPagedByNameAsync(long userId, int page, int pageSize, string name, CancellationToken)`；**不标 `[GenerateController]`**（L17-18 注释："REST 经 INotificationStore 门面暴露"）
- **消费链（映射回原实体——保守形态）**：
  - `NotificationStore.GetListAsync`（L38-55）→ 无 name 时 `_userNotificationDataService.GetListPagedByUserIdAsync`；有 name 时 `_userNotificationViewDataService.GetPagedByNameAsync` → 视图行**映射回 `UserNotificationEntity`**（Id/UserId/NotificationId/State/ReadTime/CreateTime 六字段——**Name/Severity/DisplayName 三列被丢弃**，注释 L46 自认"由 GraphQL 路径消费"）
  - `INotificationStore.GetListAsync` 签名 → `IReadOnlyList<UserNotificationEntity>`
- **DTO 已生成**：`UserNotificationViewDto`（九字段——含 Name/Severity/DisplayName 完整视图面）

### 2.3 关键机制事实（REST 直接暴露路径）

| 项 | 事实 | 证据 |
|----|------|------|
| VEntity DataService 不支持 REST | `isDataService`（`typeSymbol.Name.EndsWith("DataService")`）门控 + `fromDataService` → early-return，不生成 Controller/Interface/Decorator 三件套；标 `[GenerateController(FromDataService=true)]` 也无效 | `ControllerGenerator.cs` L627/L629-642/L801 注释"⚠️ VEntity 不支持 REST 端点" |
| REST 唯一路径 = Service 包装类 | 非 DataService 命名 → 走 Service 模式（public async 方法自动纳入契约）；继承 `DomainServiceBase`/`DomainReadOnlyDataServiceBase` + 标 `[GenerateController]`（非 FromDataService） | `ControllerGenerator.cs` L686/722 `ExtractTargetFromSymbol` |
| GraphQL 独立已就绪 | `CollectGraphQLQueryableEntities` 独立扫描 `[DomainGenerateCode].ExposeGraphqlQuery`（默认 = IsView，VEntity 默认 true）——两 VEntity 已自动生成 resolver | `ControllerGenerator.cs` L3070-3124 |
| VEntity DTO 一等公民 | `UserRoleViewDto`/`UserNotificationViewDto` 已生成（含完整视图列），可直接作为 REST 返回类型 | `.g.cs` 确认 |

---

## 三、优化设计

### 3.1 Identity —— 新增 `UserRoleViewQueryService`（REST Service 包装类）

```csharp
// 非 DataService 命名（isDataService 门控规避）→ [GenerateController] Service 模式
// ⚠️ C1-high 修订：userId 从 IDomainUser 取当前用户（不暴露参数——防 IDOR 越权查他人角色）
[GenerateController]
public partial class UserRoleViewQueryService(IDomainUser user, UserRoleViewDataService viewDataService)
    : DomainServiceBase(user)
{
    /// <summary>取当前用户角色视图（VEntity DTO 一等公民——含 DisplayName/IsSystemRole 完整字段）。
    /// ⚠️ 仅本人（userId 从 IDomainUser 解析——不信任客户端传参，防水平越权）。</summary>
    [ApiExpose(RestRoute = "/api/identity/user-roles", RestMethod = "GET")]
    public async Task<List<UserRoleViewDto>> GetMyRolesAsync(CancellationToken ct = default)
    {
        var userId = ResolveCurrentUserId();   // 从 IDomainUser 取当前用户（仅本人）
        var views = await viewDataService.GetRolesByUserIdAsync(userId, ct);
        return views.Select(v => UserRoleViewDto.FromEntity(v)).ToList();
    }

    /// <summary>解析当前用户 id（IDomainUser.UserId string → long）。</summary>
    private long ResolveCurrentUserId()
        => long.TryParse(User.UserId, out var id) ? id : throw new UnauthorizedAccessException("当前用户上下文无效");
}
```

- **继承选择**：`DomainServiceBase`（非 DataService——不直接持有 DAC，注入 VEntity DataService 委托）——规避 `isDataService` 命名门控 + 职责分离（REST 暴露层 / 数据访问层）。
- **⚠️ 安全守卫（oracle3 C1-high）**：**userId 从 `IDomainUser` 取当前用户**（方法签名去掉 userId 参数）——`GetMyRolesAsync` 仅本人，不信任客户端传参（防 IDOR 越权查他人角色）；管理员全量查询另设 `[RequirePermission]` 守卫端点（v0.1.0 不做，留待管理需求）。
- **partial（oracle3 C5-med）**：`partial class`（对齐 `IdentityAuthService` 先例 L21——SG1 `[GenerateController]` 生成 Decorator/Controller 分部类，`sealed` 会阻断）。
- **注册（oracle3 C2-high）**：**核实 `[GenerateController]` Service 是否 SG1 自动注册**——`IdentityAuthService` 先例（V0.3.0）在 `IdentityExtensionInitializer` **无 TryAddScoped 注册**（已读源码 L35-52 确认）；实施前读 `ControllerGenerator.cs` L686/722 `ExtractTargetFromSymbol` 附近确认生成机制：**若 SG1 自动注册 → 不补 TryAddScoped（对齐先例）**；若需手动 → 解释机制差异。**不得与先例矛盾地默写 TryAddScoped**。

### 3.2 Notifications —— 新增 `UserNotificationViewQueryService`（REST Service 包装类）

```csharp
// ⚠️ C1-high 修订：userId 从 IDomainUser 取当前用户；C4-med 修订：name 可空（无 name = 查全部收件箱）
[GenerateController]
public partial class UserNotificationViewQueryService(IDomainUser user, UserNotificationViewDataService viewDataService)
    : DomainServiceBase(user)
{
    /// <summary>取当前用户收件箱（VEntity DTO 一等公民——含 Name/Severity/DisplayName 完整字段）。
    /// ⚠️ 仅本人（userId 从 IDomainUser 解析）；name 可空——null = 查全部（对齐门面语义）。</summary>
    [ApiExpose(RestRoute = "/api/notifications/inbox", RestMethod = "GET")]
    public async Task<List<UserNotificationViewDto>> GetMyInboxAsync(
        int page, int pageSize, string? name = null, CancellationToken ct = default)
    {
        var userId = ResolveCurrentUserId();
        if (name is null)
        {
            // 查全部——经 UserNotificationEntityDataService（对齐门面无 name 路径）
            var items = await _entityDataService.GetListPagedByUserIdAsync(userId, page, pageSize, ct);
            return items.Select(UserNotificationViewDto.FromNotification).ToList();
        }
        var views = await viewDataService.GetPagedByNameAsync(userId, page, pageSize, name, ct);
        return views.Select(v => UserNotificationViewDto.FromEntity(v)).ToList();
    }

    private long ResolveCurrentUserId()
        => long.TryParse(User.UserId, out var id) ? id : throw new UnauthorizedAccessException("当前用户上下文无效");
}
```

- 同 3.1 设计（`DomainServiceBase` + 注入 VEntity DataService + **注册机制核实后对齐先例**）。
- **⚠️ name 语义割裂修复（oracle3 C4-med）**：`name` 可空——null = 查全部收件箱（委托 `UserNotificationEntityDataService`，对齐门面 `NotificationStore.GetListAsync` 无 name 路径语义）；**REST 面 ≥ 门面面**。

### 3.3 门面内部链路**保留不变**（划界）

- `UserStore.GetRolesAsync` / `NotificationStore.GetListAsync` **不改**——内部仍映射回实体（`IdentityRoleProvider` Scoped 缓存 / `IdentityUserHelperBase` / `NotificationStore` 无 name 路径业务消费需要实体）；现有测试断言零破坏。
- 新 REST Service 与门面**并存**——外部 API 经新端点消费 VEntity DTO（展示面完整），内部业务经门面（实体语义）。

---

## 四、裁定点（需评审确认）

| # | 裁定点 | 建议 | 理由 |
|---|--------|------|------|
| **C1** | REST 暴露形态：新建 Service 包装类（非 DataService 命名）？ | **新建 `UserRoleViewQueryService` / `UserNotificationViewQueryService`** | **机制强制**——VEntity DataService 标 `[GenerateController(FromDataService=true)]` 无效（isDataService 门控 early-return，ControllerGenerator.cs L629-642）；Service 模式是唯一路径 |
| **C2** | Service 包装类基类选择：`DomainServiceBase` vs `DomainReadOnlyDataServiceBase`？ | **`DomainServiceBase`**（注入 VEntity DataService 委托） | 命名规避 isDataService 门控（`DomainReadOnlyDataServiceBase` 派生类名仍可能含 "DataService" 后缀触发门控）+ 职责分离（Service=REST 暴露层、DataService=数据访问层）+ 红线合规（不直接持有 DAC） |
| **C3** | 门面内部链路（`IUserManager.GetUserRolesAsync`/`INotificationStore.GetListAsync`）是否改返回 VEntity DTO？ | **保留实体返回（零改动）** | 门面被 `IdentityRoleProvider`（Scoped 缓存 roles）/`IdentityUserHelperBase`（登录填充）/`NotificationStore`（无 name 路径）业务消费，需实体 Id/Name；改签名破坏既有断言 + 波及业务链路——**REST 新端点已提供 DTO 面，"直接暴露"目标达成，无需破坏门面** |
| **C4** | `UserNotificationView` 的 `ExposeGraphqlQuery=true` 是否回溯审查？ | **保留 true（本期不动）+ 列入 §九 F4/F11 回溯审查清单** | oracle3 方案2 C-med-4 已提示：收件箱（UserId/State/ReadTime/Name）私密性与 Approval/PrintTemplates 同属敏感面——但本方案**新增 REST 端点同样暴露收件箱**，须一并评估；本期保留既有决策，回溯审查归框架候选 F4（OwnerFilter）/F11（默认收敛）触发时统一处理 |
| **C5** | **IDOR 防护：REST 端点 userId 参数是否裸露？**（oracle3 C1-high） | **从 `IDomainUser` 取当前用户**（方法签名去 userId 参数，`GetMyRolesAsync`/`GetMyInboxAsync` 仅本人） | 开篇 §5.2 P4 判据"SQL 视图无法感知仅本人——暴露面须应用层守卫"+ 开篇 §九 F6 OwnerFilter 触发条件——本方案主动新增 REST 端点 = 主动触发 F6 场景，**须在引入时同步处理**（新增风险不应推迟到框架候选）；管理员全量查询另设 `[RequirePermission]` 守卫端点（v0.1.0 不做，留待管理需求） |
| **C6** | **注册机制：`TryAddScoped` 与 IdentityAuthService 先例不一致？**（oracle3 C2-high） | **实施前核实 SG1 自动注册边界**——`IdentityAuthService` 先例（V0.3.0）在 `IdentityExtensionInitializer` **无 TryAddScoped 注册**（L35-52 已读源码确认） | 读 `ControllerGenerator.cs` L686/722 `ExtractTargetFromSymbol` 确认：若 SG1 自动注册 `[GenerateController]` Service → **不补 TryAddScoped（对齐先例）**；若需手动 → 解释 AuthService 例外。**不得与先例矛盾地默写 TryAddScoped** |
| **C7** | **Notifications name 语义：REST 端点能否查全部收件箱？**（oracle3 C4-med） | **`name` 可空**——null = 查全部（委托 `UserNotificationEntityDataService`） | 门面 `NotificationStore.GetListAsync` name 可空（null 走 `GetListPagedByUserIdAsync` 查全部）；REST name 必填 = 语义割裂（REST 面 < 门面面）——修复后 REST 面 ≥ 门面面 |

---

## 五、影响面与划界

| 面 | 变更 | 说明 |
|----|------|------|
| Identity 新增 | `UserRoleViewQueryService` + `UserRoleViewDto` REST 暴露 | 新端点（GET `/api/identity/user-roles` 仅本人），VEntity DTO 一等公民 |
| Identity 门面 | **零改动** | `UserStore.GetRolesAsync`/`UserManager.GetUserRolesAsync`/`IdentityRoleProvider`/`IdentityUserHelperBase` 内部链路不变 |
| Notifications 新增 | `UserNotificationViewQueryService` + `UserNotificationViewDto` REST 暴露 | 新端点（GET `/api/notifications/inbox` 仅本人，name 可空），含 Name/Severity/DisplayName |
| Notifications 门面 | **零改动** | `NotificationStore.GetListAsync`/`GetUnreadAsync` 不变 |
| 安全守卫 | **新增（C5）** | 两 QueryService 从 `IDomainUser` 取当前用户（仅本人）——**不暴露 userId 参数**，防水平越权（IDOR）；`UnauthorizedAccessException` 当用户上下文无效 |
| GraphQL | **零改动** | 两 VEntity `ExposeGraphqlQuery=true` 已就绪 |
| 生产部署 | 新增文档章节 | 两视图 ViewSql 已在既有使用指南（V0.2.0 已落）；REST 新端点文档补入 |

---

## 六、测试锚点与新增用例

### 既有断言须保持全绿（门面链路零改动 → 天然保留）

- **Identity**：`FreeSqlUserStoreTests.cs` `AssignRole_And_GetRoles_Work`（L157，`GetRolesAsync` 返回 Single + Name=="Admin"）/ `Delete_User_CascadesRoleAssignment`（L150，空）/ `AssignRole_Duplicate_IsIdempotent`（L173）；`IdentityV03Tests.cs` `RoleProvider_GetRoles_RealTimeLookup`（L135）+ `RoleProvider_AssignRoles_ImmediateEffect`（L153）——全部经门面链路，零破坏。
- **Notifications**：`NotificationStoreTests.cs` `GetUnread_ReturnsOnlyUnread`（L13）/ `GetList_Paged`（L37）——门面链路不变，天然保留。

### 新增用例

| # | 用例 | 验证点 |
|---|------|--------|
| N1 | `UserRoleViewQueryService` REST 暴露：`GetMyRolesAsync` 返回 `List<UserRoleViewDto>`（含 DisplayName/IsSystemRole） | VEntity DTO 直接暴露 + FromEntity 映射 + **仅本人**（IDomainUser 解析） |
| N2 | `UserNotificationViewQueryService` REST 暴露：`GetMyInboxAsync` 分页返回含 Name/Severity/DisplayName | 三列不再丢弃 + **仅本人** |
| N2b | `GetMyInboxAsync` name 可空：null 查全部收件箱 | name 语义割裂修复（C4-med/C7） |
| N3 | 门面链路回归：`IUserManager.GetUserRolesAsync` 仍返回 `RoleEntity` | 零破坏 |
| N4 | Service 注册可解析：构造容器 resolve `UserRoleViewQueryService`/`UserNotificationViewQueryService` | **DI 注册可解析**（oracle3 C6-med 修订——当前测试宿主手动构造模式不触发 ControllerGenerator；**REST 端点生成验证归消费方集成测试**，非本扩展测试范围） |
| N5 | **越权防护（oracle3 C1-high）**：模拟 `IDomainUser` 无 userId / 非本人上下文 → `GetMyRolesAsync`/`GetMyInboxAsync` 抛 `UnauthorizedAccessException` 或返回空（不泄露他人数据） | 防水平越权（IDOR） |

---

## 七、实施清单

1. **Identity**：
   - 新增 `Services/UserRoleViewQueryService.cs`（`[GenerateController]` + `DomainServiceBase` + **`partial class`** + 注入 `UserRoleViewDataService` + **`GetMyRolesAsync`（仅本人，无 userId 参数——C5）** + `[ApiExpose(RestRoute="/api/identity/user-roles", RestMethod="GET")]`）
   - **注册机制核实（C2-high/C6）**：读 `ControllerGenerator.cs` Service 模式生成段——若 SG1 自动注册则不补 TryAddScoped（对齐 `IdentityAuthService` 先例）；需手动才补
   - 门面链路零改动
2. **Notifications**：
   - 新增 `Services/UserNotificationViewQueryService.cs`（同范式 + **`GetMyInboxAsync` name 可空（null 查全部）——C4-med/C7** + `[ApiExpose(RestRoute="/api/notifications/inbox", RestMethod="GET")]`）
   - 注册机制同 Identity 核实
3. **回归**：`dotnet build` slnx 0 错误 + 全量测试绿（29 项目 1394 用例基线 + 新增）
4. **文档**：两扩展使用指南补"REST 直接暴露"章节（新端点 + VEntity DTO 说明）+ README 版本演进
5. **落档**：教学系列 §八 路线图更新（Identity/Notifications ⚪ 待立项 → ✅ 方案已评审）+ 本篇案例编号 03

---

## 八、验收标准

- [ ] 新 REST 端点返回 VEntity DTO（`UserRoleViewDto`/`UserNotificationViewDto`——含 JOIN 携带列完整面）
- [ ] `UserRoleViewDataService`/`UserNotificationViewDataService` 标 `[GenerateController(FromDataService=true)]` **无效机制规避**（采用 Service 包装类——C1）
- [ ] **仅本人防护（C5）**：`GetMyRolesAsync`/`GetMyInboxAsync` 从 `IDomainUser` 取当前用户（无 userId 参数）——越权用例 N5 通过
- [ ] **name 可空（C7）**：`GetMyInboxAsync` null = 查全部收件箱
- [ ] **注册机制与先例一致（C6）**：核实 SG1 自动注册边界——TryAddScoped 不默写（对齐 IdentityAuthService）
- [ ] 门面内部链路零改动（`UserStore.GetRolesAsync`/`NotificationStore.GetListAsync`/`IdentityRoleProvider`/`IdentityUserHelperBase` diff 为空）
- [ ] GraphQL 零改动（既有 `ExposeGraphqlQuery=true` 保持）
- [ ] 既有断言全绿 + 新增 N1-N5 用例绿
- [ ] `lsp_diagnostics` 变更文件干净
- [ ] 使用指南补 REST 暴露章节

---

## 九、实施前补全项

| # | 项 | 说明 |
|---|----|------|
| P1 | `UserRoleViewDto.FromEntity` / `UserNotificationViewDto.FromEntity` 确认存在 | SG1 生成 `.g.cs` 含 `FromEntity(TEntity)` 静态方法——实施前核实签名（含 `IDomainDto<TEntity>` 派生约束）；`UserNotificationViewDto` 需 `FromNotification`（实体→DTO）辅助（C4-med 查全部路径用） |
| P2 | `[GenerateController]` Service 模式契约面 + 注册机制核对 | **读 `ControllerGenerator.cs` L686/722 `ExtractTargetFromSymbol` + 生成段**：确认 Service 模式 public async 方法纳入契约 + **SG1 是否自动注册**（决定 TryAddScoped 去留——C2-high/C6）；`[ApiExpose]` 路由/方法显式标注（对齐 `IdentityAuthService` 先例） |
| P3 | `DomainServiceBase` 构造签名核对 | 非泛型主实现 `DomainServiceBase(IDomainUser user)`（V4.6.4）——`UserRoleViewQueryService(IDomainUser user, UserRoleViewDataService viewDataService) : base(user)` 构造传递确认 |
| P4 | 测试宿主 DI 注册 | 新 Service 类在测试宿主（`IdentityTestHost`/`NotificationTestHost`）手动构造或 DI 注册——对齐 `UserRoleViewDataService` 手动构造先例（IdentityTestHost.cs L43）；**N4 降级为"DI 注册可解析"**（REST 端点生成验证归消费方集成测试——C6-med） |
| P5 | README 路线图调整 | Identity README §六 V0.4.0 原条目（"IAccountPasswordManager 适配器 + Permissions 深度集成 + 多租户"）与本方案主题不同——**V0.4.0 改为"REST 直接暴露升级"**，原条目顺延/合并（oracle3 P4） |

---

## 变更记录

| 日期 | 版本 | 变更内容 |
|------|------|---------|
| 2026-09-30 | v0.1.0-draft | 初始草案——基于 bg_1dc747d4（Identity+Notifications VEntity 直接暴露探针）机制事实：VEntity DataService 不支持 REST（isDataService 门控）、GraphQL 独立已就绪、Service 包装类为唯一 REST 路径 |
| 2026-09-30 | v0.1.1 | **oracle3 评审 PASS WITH CONDITIONS（`bg_a62f3ca0`）7 条件全修订**——C1-high（**IDOR 防护：userId 从 IDomainUser 取当前用户**——方法去参数仅本人，越权用例 N5；对齐开篇 §5.2 P4 判据）+ C2-high（**注册机制核实**——IdentityAuthService 先例无 TryAddScoped，不得默写）+ C3-med（**`[ApiExpose]` 路由显式定案**——`/api/identity/user-roles` + `/api/notifications/inbox`）+ C4-med（**name 可空修复**——null 查全部收件箱，REST 面 ≥ 门面面）+ C5-med（**sealed → partial**——对齐 IdentityAuthService L21）+ C6-med（**N4 降级为 DI 注册可解析**——当前测试宿主不触发 ControllerGenerator）+ C7-low（内存映射接受，远期投影优化）；P1-P5 建议纳入 |

---

## 评审记录

| 日期 | 评审人 | 结论 | 修订 |
|------|--------|------|------|
| 2026-09-30 | oracle3 | **PASS WITH CONDITIONS**——C1-C4 裁定方向正确（C1/C2 机制强制、C3 门面划界合理）；7 条件（C1-high/C2-high + C3-C7 med/low）+ 4 建议（P1-P4） | 全部条件修订完成（§三/§四/§五/§六/§七/§八/§九 + 变更记录 v0.1.1）；C1-high/C2-high 修订后须复评 |
