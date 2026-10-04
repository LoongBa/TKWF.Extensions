# TKWF.Ext.Notifications 通知中心扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.6.0 + **V0.6.1（V4.10.55 多实现集合守卫工厂，ADR92/T3 闭环——InboxNotifier TryAddEnumerableConstructible + 继承 DomainServiceBase）** | **框架**: .NET 10

**核心约束**: 三层数据模型（Notification 发布态 → UserNotification 收件箱行 → NotificationSubscription 订阅）、事件驱动通知（D15 事件总线高层组合）、多通道路由（v0.2.0 已实施：定义 UseChannels 声明 + Email 通道 best-effort）、用户偏好路由 + 逐用户权限门控（v0.3.0 已实施）、SignalR 实时推送通道（v0.4.0 独立包 `TKWF.Ext.Notifications.SignalR`，服务端非 UI）、REST 直接暴露（v0.5.0 已实施：VEntity DTO 一等公民）、SG1 声明式实体、**领域自治根治（V0.5.1：门面 `AddConstructibleService` + `User.Use<接口>()`）**

---

## 一、需求分析 (Demand Analysis)

站内通知是企业应用高频横切能力（订单状态、审批结果、系统公告）。TKWF 以 **事件驱动 + 显式发布双路径** 提供统一通知中心——业务代码零感知（事件驱动）或一行显式发布（`INotificationPublisher`）。

- **消费场景**：订单创建/发货通知、审批通过/驳回、系统公告、账户异常提醒。
- **集成模式**：引用扩展 → `[TKWFEnabledExtension]` 白名单 → 注册通知定义（`INotificationDefinitionProvider`）→ 发布（事件 handler 或显式调用）。
- **事件驱动（D15 §5.7）**：通知 = 事件总线的高层组合——业务发布领域事件，`[DomainEventHandler]` 消费并调 `INotificationPublisher`。

## 二、设计原理 (Design Principles)

### 1. 数据模型三层（Oracle C2/m1）

| 实体 | 表名 | 职责 |
|------|------|------|
| `NotificationEntity` | `Notification` | 发布态——通知内容/严重级别/实体关联（一份通知一次写入） |
| `UserNotificationEntity` | `UserNotification` | 收件箱行——每收件人一行（UserId + NotificationId + 已读状态） |
| `NotificationSubscriptionEntity` | `NotificationSubscription` | 订阅关系——定义级/实体级，唯一约束防重复（m1） |

### 2. 派发语义（PublishAsync）

```
userIds = null   → 从订阅者解析收件人（定义级 + 实体级 entityTypeName/entityId 匹配）
userIds = []     → 空操作（直接返回）
userIds = [x,y]  → 显式收件人（去重 + 排除 excludedUserIds）
```

- **发布方权限门控（C1 修订）**：定义 `RequirePermission` 仅校验发布方（当前用户 `IPermissionChecker`），非逐收件人；IPermissionChecker 未注册（Permissions 未启用）→ 明确抛异常提示
- **事务边界（C4）**：`ITransactionManager.Begin()` 包裹——Notification 行 + 全部 inbox 行原子提交，失败整体回滚

### 3. 通道抽象与多通道路由（C5，v0.2.0 已实施）

```
NotificationDeliveryRequest（NotificationId + UserId + Channel + DataJson）
  → INotificationNotifier.DeliverAsync(request)   // 通知器 owns 投递副作用
  → InboxNotifier：写 UserNotification 行（幂等：同 UserId+NotificationId 跳过）——参与发布事务 C4
  → EmailNotifier（v0.2.0 Email 通道，best-effort M1）：经 IEmailSender 发送
  → SignalRNotifier（远期）：复用同一请求模型
```

- **多通道路由**：通知定义经 `UseChannels(params string[] channels)` 声明投递通道（默认 `["Inbox"]`，按声明顺序去重存储）；
  `NotificationPublisher` 逐通道构造 `NotificationDeliveryRequest` 并匹配 `notifier.Name == channel` 投递——
  未声明通道名的 notifier 自然跳过；不存在的通道名不校验（投递时无匹配 notifier 即跳过，不抛异常）
- **异常语义（M1 修订）**：Inbox 通道参与发布事务（C4）——写入失败必须传播异常触发回滚；
  外部通道（Email）best-effort——前置缺失/投递失败 LogWarning 自行处理，不重抛阻塞发布流程
- **Email 通道前置**：消费方须注册 `IUserEmailProvider`（查邮箱）+ 启用 Emailing 扩展（注册 `IEmailSender`）；
  任一缺失 → 跳过投递不失败（IServiceProvider 可空延迟解析，对齐 C1 模式）

### 4. 事件驱动（v0.1.0 先例）

Notifications 是**第一个消费事件总线的扩展**（D15 §5.7 落地）——handler 是消费方代码：

```csharp
[DomainEventHandler]
public class OrderNotificationHandler(INotificationPublisher publisher)
    : ILocalEventHandler<OrderCreatedEvent>
{
    public async Task HandleEventAsync(OrderCreatedEvent eventData)
        => await publisher.PublishAsync("OrderCreated", new NotificationData(...), userIds: [eventData.BuyerId]);
}
```

- **D15 post-commit 语义**：handler 在业务事务提交后运行；通知写库失败 → 日志记录不重抛
- **外部 I/O**（v0.2.0 Email/SignalR）：事件应标记 `[DistributedEvent]` 走 Outbox，handler 实现 `IDistributedEventHandler<T>`

## 三、核心组件清单 (Component List)

> **V0.5.1（领域自治根治，ADR90 正确路线）注册形态四态**（消费方白名单启用后 `ConfigureServices` 自动接线）：
> - **`AddConstructibleService` 门面**——`INotificationStore`/`INotificationSubscriptionManager`/`INotificationPreferenceManager`/`INotificationPublisher`（4 标准门面继承 `DomainServiceBase` + 接口 `: IDomainService` + `[DiContractIgnore]`）：接口注册为可构造守卫工厂（CurrentAopUser 守卫——域作用域外解析即抛）+ 实现类注册为 throw-factory；消费方统一经 `User.Use<接口>()` 解析（AOP 路径）。
> - **`TryAddSingleton` 定义管理**——`INotificationDefinitionManager`（m2：定义启动时收集后不可变）。
> - **`TryAddScoped` 接线型 + `TryAddEnumerable` 通道**——`InboxNotifier`/`EmailNotifier`（多实例收集：InboxNotifier owns UserNotification 写入 C5；EmailNotifier 接线型 ctor(IServiceProvider,ILogger) 无 user 依赖，best-effort M1）。
> - **反射 Provider 扫描**——`[NotificationDefinitionProvider]` 特性驱动注册（`RegisterNotificationDefinitionProviders`，M3，不动）。
>
> ✅ **V4.10.55（ADR92/T3 闭环）**：`InboxNotifier` 改 `TryAddEnumerableConstructible`（集合版守卫工厂）+ 继承 `DomainServiceBase`（删 `_user` 字段，经基类 `User` 取上下文 + `[DiContractIgnore]`）——`NotificationPublisher` 经 `User.Use<INotificationPublisher>()` 帧内创建时 ctor 注入的 `IEnumerable<INotificationNotifier>` 在帧内枚举，守卫工厂经 `CurrentAopUser` 供给；帧外枚举（控制器 `[FromServices]` 等）抛守卫（禁止形态）。`EmailNotifier` 保持接线型普通 DI（无 user 依赖，可解析）。修复原边界保留生产失败态（T3 转达 §三 实证）。

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`INotificationPublisher`** | 发布通知（定义校验 + 发布方权限门控 + 收件人解析 + 事务写入） | `NotificationPublisher`（继承 `DomainServiceBase`，`AddConstructibleService` 注册——V0.5.1） |
| **`INotificationStore`** | 收件箱查询（未读/列表/计数/已读/全部已读） | `NotificationStore`（继承 `DomainServiceBase`，`AddConstructibleService` 注册——V0.5.1；命名重组候选 `NotificationStore`→`NotificationInboxManager` 留待后续批次） |
| **`INotificationSubscriptionManager`** | 订阅管理（定义级/实体级订阅/退订，幂等） | `NotificationSubscriptionStore`（继承 `DomainServiceBase`，`AddConstructibleService` 注册——V0.5.1；命名错位候选类名 Store/接口 Manager 留待后续批次） |
| **`INotificationDefinitionManager`** | 通知定义注册/校验（Singleton） | `NotificationDefinitionManager`（接线型保留——ctor 仅 `IEnumerable<INotificationDefinitionProvider>` 无 user 依赖，TryAddSingleton 普通 DI 注册不变；接口标 IDomainService 但实现无 user 依赖——记录于整改日志） |
| **`INotificationDefinitionProvider`** | 通知定义贡献者（`[NotificationDefinitionProvider]` 扫描） | 消费方实现 |
| **`INotificationNotifier`** | 通道抽象（多实例收集：Inbox `TryAddEnumerableConstructible` V4.10.55 ADR92 + Email 接线型普通 DI） | `InboxNotifier`（V4.10.55 已改构造守卫工厂）/ `EmailNotifier` |
| **`INotificationPreferenceManager`** | 用户通道偏好管理（Get/Set/Clear + 批量预取，V0.3.0） | `NotificationPreferenceStore`（继承 `DomainServiceBase`，`AddConstructibleService` 注册——V0.5.1；命名错位候选同上） |
| **`IUserEmailProvider`** | 用户邮箱提供者（Email 通道收件地址，消费方实现） | 消费方实现（扩展不注册） |
| **`NotificationsOptions`** | 配置（`TKWF:Notifications` 节） | 内置 |
| **`NotificationsSignalRExtensionInitializer<T>`** | SignalR 通道初始器（V0.4.0，独立包）——注册 SignalRNotifier + Options（`TKWF:Notifications:SignalR`） | SignalR 包 |
| **`SignalRNotifier`** | SignalR 通道通知器（V0.4.0，独立包）——best-effort 经 `IHubContext<NotificationsHub>` 推送在线用户 | SignalR 包 |

## 四、实体表结构 (Entity Schema)

### Notification → `Notification` 表

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| Name | NVARCHAR(128) | 通知名（稳定唯一） |
| DisplayName | NVARCHAR(256)? | 显示名快照 |
| DataJson | NVARCHAR(4000)? | 通知数据（键值对 JSON） |
| Severity | INT | 严重级别（Info/Success/Warning/Error） |
| EntityTypeName | NVARCHAR(256)? | 关联实体类型 |
| EntityId | NVARCHAR(128)? | 关联实体 ID |
| CreateTime | DATETIME | 创建时间 |

### UserNotification → `UserNotification` 表

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| UserId | BIGINT | 收件人（long 约定） |
| NotificationId | BIGINT | 关联 Notification.Id |
| State | INT | 0=Unread, 1=Read |
| ReadTime | DATETIME? | 已读时间 |
| CreateTime | DATETIME | 创建时间 |

索引：`IX_UserNotification_UserState`（UserId + State）+ `IX_UserNotification_NotificationId`

### NotificationSubscription → `NotificationSubscription` 表

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| UserId | BIGINT | 订阅用户 |
| NotificationName | NVARCHAR(128) | 订阅的通知名 |
| EntityTypeName | NVARCHAR(256)? | 实体级订阅（null=定义级） |
| EntityId | NVARCHAR(128)? | 实体级订阅（null=定义级） |
| CreateTime | DATETIME | 创建时间 |

索引：`IX_NotificationSubscription_User` + **`UX_NotificationSubscription_Unique`（唯一：UserId+Name+EntityType+EntityId）**

> **已知限制（M4）**：唯一索引含 NULL 列（EntityTypeName/EntityId），标准数据库（SQL Server/PostgreSQL/SQLite）唯一约束中 NULL 视为互异——定义级订阅（两列均为 NULL）的并发幂等**仅靠应用层 check-then-insert 保证**，DB 约束不覆盖。并发重复订阅极窄竞态下可能产生重复行。v0.2.0 修复（过滤索引/哨兵列）；当前单线程/常规并发场景由应用层幂等 + 事务保证。

### NotificationPreference → `NotificationPreference` 表（V0.3.0 偏好）

| 列名 | 类型 | 说明 |
|------|------|------|
| Id | BIGINT (PK, Identity) | 主键 |
| UserId | BIGINT | 偏好用户 |
| NotificationName | NVARCHAR(128) | 偏好通知名（对齐 Notification.Name） |
| ChannelsJson | NVARCHAR(256)? | 通道列表 JSON（如 `["Email"]`；null = 回退定义级；`[]` = 不接收） |
| CreateTime | DATETIME | 创建时间 |
| UpdateTime | DATETIME | 更新时间 |

索引：**`UX_NotificationPreference_User_Notification`（唯一：UserId + NotificationName）**——每用户每通知至多一条偏好

## 五、依赖关系

```
TKWF.Ext.Notifications（主包——纯 Domain，零 Web 依赖）
├── 主框架 Domain + SG1（实体）
├── TKWF.Ext.Permissions.Abstractions（发布方权限门控 IPermissionChecker + 逐用户门控 IPermissionBatchChecker V0.3.0，ADR48 D7）
├── TKWF.Ext.Emailing.Abstractions（Email 通道契约 IEmailSender/EmailMessage，ADR48 D7——不引 Emailing 实现）
└── 事件总线（[DomainEventHandler] + ILocalEventHandler）

TKWF.Ext.Notifications.SignalR（v0.4.0 独立包——按需引入，不引则零 Web 负担）
├── TKWF.Ext.Notifications（通道契约 INotificationNotifier/NotificationDeliveryRequest）
└── FrameworkReference Microsoft.AspNetCore.App（SignalR 类型共享框架——零 NuGet 包，对齐 HealthCheck 接线先例）
```

## 六、架构演进路线 (Architecture Roadmap)

### V0.1.0（已实施）
- 站内通知核心：发布/收件箱/订阅/定义注册 + 事件驱动先例 + 通道抽象（inbox-only）
- Oracle 评审 PASS WITH CONDITIONS（C1-C5 修订 + m1-m7 处理）

### V0.2.0（已实施：VEntity 跨表查询升级 + 多通道路由）
- **`GetListAsync(name)` VEntity 化**：新增 `UserNotificationView`（VEntity，JOIN `UserNotification` → `Notification` 单查询下推 DB），替代两步查询（先按 name 取 Notification.Id 集合再按集合过滤）——消除两次往返 + IN 子句，顺带返回通知名/严重级别/显示名（GraphQL 路径可用）
- 手写只读 DataService `UserNotificationViewDataService`（`DomainReadOnlyDataServiceBase` + `IEntityReadOnlyDAC<T>`，红线合规）
- Store 接口签名不变（视图行映射回 `UserNotificationEntity`），消费方零迁移
- **生产部署要求**：框架 SyncViewsAsync 只跑开发环境建视图；**生产需 DBA 手动执行 ViewSql**（PG 默认方言，SQL Server 等需补变体）
- **多通道路由**：`NotificationDefinition.UseChannels(params string[])` 声明投递通道（默认 `["Inbox"]`，顺序去重、空数组抛 ArgumentException）；
  `NotificationPublisher` 按定义 Channels 逐通道投递（替换 v0.1.0 硬编码 Inbox 过滤），C4 事务边界不变
- **Email 通道先例（best-effort M1）**：`EmailNotifier`（Name="Email"）延迟可空解析 `IEmailSender` + `IUserEmailProvider`
  ——Emailing 扩展未启用或消费方未实现邮箱提供者时构造不失败、投递 LogWarning 跳过；发送异常 catch 不重抛阻塞发布流程
- **消费方接线**：注册 `IUserEmailProvider`（从用户存储查邮箱）+ `UseChannels("Inbox","Email")` 声明；Email 通道失败不影响发布事务（C4）与 inbox 写入

### V0.3.0（已实施：用户偏好路由 + 逐用户权限门控）
- **用户偏好路由**：新增 `NotificationPreference` 独立偏好表（UserId+NotificationName 唯一 `UX_NotificationPreference_User_Notification`；ChannelsJson 存通道列表 JSON）——用户通道偏好**覆盖定义级 UseChannels**（无偏好回退定义级零迁移；空列表 `[]` = 显式不接收该通知）
- **`INotificationPreferenceManager`**（Scoped）：`GetChannelsAsync`/`SetAsync`/`ClearAsync` + **`GetChannelsBatchAsync` 批量预取**（Oracle P2-1：收件人解析后、事务前一次预取，避免事务内 N 次往返）；`NotificationPreferenceStore` 经 `NotificationPreferenceEntityDataService`（SG1 DataService）委托持久化（红线合规）
- **Publisher 改造**：发布流程 收件人解析 → **逐用户权限门控（V0.3.0）** → **偏好批量预取覆盖** → 事务写入；最终通道列表 = 有偏好用偏好（空列表不投递）、无偏好回退定义级
- **逐用户权限门控（委托 Permissions v0.9.0 `IPermissionBatchChecker`）**：定义 `RequirePermission(...)` 时仅向有权限收件人投递（Oracle P1-4 定稿——不直连 IPermissionStore、不定义角色解析器、不重实现角色回退，Admin.All/fail-closed/用户→角色回退归 Permissions `PermissionChecker` 单一真相源）；`IPermissionBatchChecker` 未注册 → LogWarning 跳过门控（降级仅发布方 C1，Oracle P2-2）；顺序 = 先权限过滤 → 再偏好覆盖（Oracle P1-3）
- **顺序语义**：偏好 `["Email"]`（排除 Inbox）→ 订阅者不在收件箱出现（用户主动降噪，Oracle P2-2 显式化）
- **前置依赖**：Permissions v0.9.0 `IPermissionBatchChecker`（Oracle P1-1 裁定现扁平并集 API 无法按用户归因，须 Permissions 补齐多用户 API）
- **测试**：61/61（v0.2.0 48 + v0.3.0 +13：偏好 9 + 权限门控 4）

### V0.4.0（已实施：SignalR 实时推送通道——独立包 `TKWF.Ext.Notifications.SignalR`，服务端非 UI）
- **独立包**：`TKWF.Ext.Notifications.SignalR`（对齐 BackgroundJobs.Quartz 拆包先例）——主包保持纯 Domain 零 Web 依赖；`FrameworkReference Microsoft.AspNetCore.App` 复用 SignalR 类型（**零 NuGet 包**，共享框架，对齐 HealthCheck 接线先例）
- **组件**：`NotificationsHub`（空 Hub 类型锚，server-side push only）+ `SignalRNotifier`（`INotificationNotifier`，`Name="SignalR"`，best-effort M1：延迟可空解析 `IHubContext<NotificationsHub>`——未 `AddSignalR()` 构造不失败投递 LogWarning 跳过）+ `SignalRNotificationPayload`（精简 DTO）+ `NotificationsSignalROptions`（`TKWF:Notifications:SignalR` 节：MethodName/Path/AllowAnonymous）+ `NotificationsSignalRExtensionInitializer`（[TKWFExtension] + 消费方白名单 `[TKWFEnabledExtension]` 双声明）+ **`NotificationsHubWebExtension`**（v4.10.45 收敛迁移——旧 `SignalREndpointExtensions.MapTkfwNotificationsHub` 静态方法已删除；`ConfigureServices` 内 `AddSignalR()` D6 边界内聚 + Options 绑定，`ConfigureEndpoints` 内 `MapHub`）
- **通道接入零改动主包**：Publisher 按 `notifier.Name == channel` 匹配投递——`UseChannels("Inbox","SignalR")` 或用户偏好 `["SignalR"]` 触发；未引包/未白名单/未 AddSignalR → 自然跳过（既有「未注册通道跳过」语义）
- **装配（v4.10.45）**：消费方 `UseWebExtensions(e => e.Add<NotificationsHubWebExtension>())` 一次声明——`AddSignalR` 宿主注册（幂等，D6 边界语义推移：消费方 → 扩展）+ Hub 端点映射 + 授权元数据全部内聚；`SignalRNotifier` 业务注册仍留 Domain 钩子（领域自治）
- **用户标识契约**：`Clients.User(userId.ToString(CultureInfo.InvariantCulture))`——默认对齐 `DefaultUserIdProvider`（`ClaimTypes.NameIdentifier` claim = userId InvariantCulture 字符串）；非标准 claim → 消费方自定义 `IUserIdProvider`
- **端点认证**：`NotificationsHubWebExtension.ConfigureEndpoints` 默认 `RequireAuthorization`（通知敏感须登录；`AllowAnonymous` 可配）；SignalR token 认证（WebSocket query string `?access_token=`）由消费方 `AddJwtBearer` events 处理（扩展只追加授权元数据不实现认证，接线型边界）
- **边界**：离线不补推（Inbox 是真相源，前端重连后拉取未读）；不做 backplane（多实例各推各的连接，消费方按需 Redis 背板）；不含前端 JS/TS 客户端（指南给示例）；不做非 Web 客户端推送
- **测试**：12/12（SignalRNotifier 8 单测 + 3 集成——双通道路由/偏好覆盖×通道联动/SignalR-only + Options/MapHub 默认值）；全量 **1337/1337**（28 项目）回归绿；Oracle 评审 PASS WITH CONDITIONS（6 P2 全部落实）
- **ADR**：`ADR-Notifications-SignalR通道-拆包与接线设计`（拆包/FrameworkReference/插入式实现/best-effort/用户标识/接线归消费方/端点认证/白名单位置/Options 绑定——Oracle 6 P2 修订纳入）

### V0.5.0（已实施：REST 直接暴露——VEntity DTO 一等公民）
- **`UserNotificationViewQueryService`**（`[GenerateController]` + `DomainServiceBase`，消费方 SG1b 自动注册——无 TryAddScoped）：
  `GET /api/notifications/inbox` 返回**当前登录用户**收件箱分页（`UserNotificationViewDto`——含 Name/Severity/DisplayName 完整 JOIN 字段，替代"视图行映射回原实体"保守形态——旧路径丢弃三列）
- **name 可空（语义对齐）**：null = 查全部收件箱（对齐门面 `INotificationStore.GetListAsync` 无 name 语义，REST 面 ≥ 门面面）；两路径均走 VEntity 视图（`UserNotificationViewDataService` 增 `GetPagedAsync`）——JOIN 携带列不再丢弃（REST 面 > 门面面）
- **仅本人防护（防 IDOR）**：userId 从 `IDomainUser` 解析（服务端上下文），不信任客户端传参；用户上下文无效 → `UnauthorizedAccessException`；管理员全量查询另设 `[RequirePermission]` 守卫端点（留待管理需求）
- **门面链路零改动**：`INotificationStore.GetListAsync` 仍返回 `UserNotificationEntity`；`UserNotificationViewDataService` public 化（公开 ctor 依赖 + 只读查询面可注入）
- **收件箱私密性回溯审查注记**：`ExposeGraphqlQuery` 是否收敛/加 OwnerFilter 归框架候选（F4/F11）触发时统一处理（方案 03 §九）
- **生产部署**：`vw_UserNotificationView` 视图 ViewSql 沿用 V0.2.0（生产 DBA 手动建视图）

### V0.5.1（已实施：领域自治根治，ADR90 正确路线——4 门面继承 DomainServiceBase + AddConstructibleService）
- **4 标准门面整改**（`NotificationStore`/`NotificationSubscriptionStore`/`NotificationPreferenceStore`/`NotificationPublisher`）：继承 `DomainServiceBase`（经基类 `User` 获取用户上下文——IDomainUser 永不注册 DI，旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败 v0.3.3 同根缺陷）+ `[DiContractIgnore]`（运行时手写注册豁免 DI001）；`_user` 字段删改基类 `User`，DataService 经 `User.Use<XxxDataService>()` NoAop 懒加载（DI004 零豁免）；注册由 `TryAddScoped` 改 **`AddConstructibleService`**（接口可构造守卫工厂——CurrentAopUser 域作用域外解析即抛 + 实现类 throw-factory），消费方统一经 `User.Use<接口>()` 解析
- **测试宿主走生产路径**：真实 DI（扩展 ConfigureServices + FreeSql 基础设施 + IEntityDAC Singleton）→ `DomainUser<TestUserInfo>.BindScope` → `user.Use<接口>()` AOP 路径；Initializer 注册形态断言（守卫工厂 + throw-factory + 域外抛，新增 2 用例）；**75 → 77 用例全绿**
- **边界保留组（本批不改，记录于整改日志）**：`InboxNotifier`/`EmailNotifier`（TryAddScoped<具体类> + TryAddEnumerable 多实现集合）——**框架缺口候选（T3 转达）**：多实现集合中 DomainServiceBase 派生实现的 user 供给依赖框架机制（普通 DI 构造 ctor(IDomainUser) 时 IDomainUser 永不注册 → 生产解析失败），对齐 MFA `IMfaMethod` 处理保留 + 记录；`NotificationDefinitionManager`（接口标 IDomainService 但实现无 user 依赖）接线型保留（TryAddSingleton 普通 DI，不继承基类——skill §4.7 心得 5）；`UserNotificationViewQueryService`/VEntity（已合规）零改动
- **命名重组候选（本批不改）**：`NotificationStore`→`NotificationInboxManager`；`NotificationSubscriptionStore`/`NotificationPreferenceStore` 类名 Store/接口 Manager 命名错位——留待后续批次

### V0.6.1（V4.10.55 多实现集合守卫工厂，ADR92/T3 闭环）
- **`InboxNotifier` 改 `TryAddEnumerableConstructible`（集合版守卫工厂）+ 继承 `DomainServiceBase`**——删 `_user` 字段（经基类 `User` 取上下文）+ `[DiContractIgnore]`；注册由 `TryAddScoped<具体类>` + `TryAddEnumerable` 改 `TryAddEnumerableConstructible<INotificationNotifier, InboxNotifier>`（实现类 throw-factory + 集合元素守卫工厂）；`EmailNotifier` 保持接线型（TryAddScoped<具体类> + TryAddEnumerable 普通 DI——ctor 无 IDomainUser，可解析）
- **修复生产失败态**（T3 转达 §三 实证，V0.5.1 边界保留组收编）——`NotificationPublisher` 经 `User.Use<INotificationPublisher>()` 帧内创建时 ctor 注入的 `IEnumerable<INotificationNotifier>` 在帧内枚举：InboxNotifier 守卫工厂经 CurrentAopUser 供给 ctor IDomainUser；帧外枚举（`GetServices` 直取）抛守卫（禁止形态）
- 测试宿主同步：`NotificationsExtensionInitializerTests` 注册断言改守卫工厂形态（Inbox = 工厂委托 / Email = 实现映射）+ `Publisher_Resolves_InFrame_WithInboxAndEmailChannels` 帧内哨兵；**78 用例全绿**

### 远期 / 评估
- 通知本地化（`ILocalizableString`，对接 D16/ADR31）
- 通知模板渲染（复用 Emailing V0.2.0 TextTemplates / PrintTemplates）
- 短信通道
- 批量派发优化（RecipientBatchSize 落地）
- 已读状态多端联动广播（`IHubContext<NotificationsHub>` 在已读 API 中复用推送已读事件——扩展点，非 v0.4.0 交付）

**文档信息**: V0.5.1 | 2026-10-04 | 关联：v0.1.0-Notifications-通知中心-开发方案.md、ADR-Notifications-事件驱动接线模式.md、ADR-Notifications-数据模型三层选型.md、ADR-Notifications-v0.3.0-用户偏好路由与权限门控.md、v0.4.0-Notifications-SignalR通道-开发方案.md、ADR-Notifications-SignalR通道-拆包与接线设计.md（主框架私有）、docs/框架实战教学/03-倒推优化-Identity与Notifications-VEntity直接暴露升级-开发方案.md（公开）、V4.10.53 ADR90 领域自治根治（主框架私有）
