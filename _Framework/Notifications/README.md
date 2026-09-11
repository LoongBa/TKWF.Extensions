# TKWF.Ext.Notifications 通知中心扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.3.0 | **框架**: .NET 10

**核心约束**: 三层数据模型（Notification 发布态 → UserNotification 收件箱行 → NotificationSubscription 订阅）、事件驱动通知（D15 事件总线高层组合）、多通道路由（v0.2.0 已实施：定义 UseChannels 声明 + Email 通道 best-effort）、用户偏好路由 + 逐用户权限门控（v0.3.0 已实施）、SG1 声明式实体

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

| **组件** | **职责** | **默认实现** |
|----------|---------|------------|
| **`INotificationPublisher`** | 发布通知（定义校验 + 发布方权限门控 + 收件人解析 + 事务写入） | `NotificationPublisher` |
| **`INotificationStore`** | 收件箱查询（未读/列表/计数/已读/全部已读） | `FreeSqlNotificationStore` |
| **`INotificationSubscriptionManager`** | 订阅管理（定义级/实体级订阅/退订，幂等） | `FreeSqlNotificationSubscriptionManager` |
| **`INotificationDefinitionManager`** | 通知定义注册/校验（Singleton） | `NotificationDefinitionManager` |
| **`INotificationDefinitionProvider`** | 通知定义贡献者（`[NotificationDefinitionProvider]` 扫描） | 消费方实现 |
| **`INotificationNotifier`** | 通道抽象（多实例收集 TryAddEnumerable：Inbox + Email） | `InboxNotifier` / `EmailNotifier` |
| **`INotificationPreferenceManager`** | 用户通道偏好管理（Get/Set/Clear + 批量预取，V0.3.0） | `NotificationPreferenceStore` |
| **`IUserEmailProvider`** | 用户邮箱提供者（Email 通道收件地址，消费方实现） | 消费方实现（扩展不注册） |
| **`NotificationsOptions`** | 配置（`TKWF:Notifications` 节） | 内置 |

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
TKWF.Ext.Notifications
├── 主框架 Domain + SG1（实体）
├── TKWF.Ext.Permissions.Abstractions（发布方权限门控 IPermissionChecker + 逐用户门控 IPermissionBatchChecker V0.3.0，ADR48 D7）
├── TKWF.Ext.Emailing.Abstractions（Email 通道契约 IEmailSender/EmailMessage，ADR48 D7——不引 Emailing 实现）
└── 事件总线（[DomainEventHandler] + ILocalEventHandler）
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

### 远期 / 评估
- 通知本地化（`ILocalizableString`，对接 D16/ADR31）
- 通知模板渲染（复用 Emailing V0.2.0 TextTemplates / PrintTemplates）
- SignalR 实时推送通道（需 AspNetCore.SignalR 依赖评估）
- 短信通道
- 批量派发优化（RecipientBatchSize 落地）

**文档信息**: V0.3.0 | 2026-09-12 | 关联：v0.1.0-Notifications-通知中心-开发方案.md、ADR-Notifications-事件驱动接线模式.md、ADR-Notifications-数据模型三层选型.md、ADR-Notifications-v0.3.0-用户偏好路由与权限门控.md（主框架私有）
