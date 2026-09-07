# TKWF.Ext.Notifications 通知中心扩展技术规范

**状态**: 核心业务扩展 (Core Business Extension) | **版本**: V0.1.0 | **框架**: .NET 10

**核心约束**: 三层数据模型（Notification 发布态 → UserNotification 收件箱行 → NotificationSubscription 订阅）、事件驱动通知（D15 事件总线高层组合）、多通道抽象（v0.1.0 inbox-only）、SG1 声明式实体

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

### 3. 通道抽象（C5）

```
NotificationDeliveryRequest（NotificationId + UserId + Channel + DataJson）
  → INotificationNotifier.DeliverAsync(request)   // 通知器 owns 投递副作用
  → InboxNotifier（v0.1.0）：写 UserNotification 行（幂等：同 UserId+NotificationId 跳过）
  → EmailNotifier / SignalRNotifier（v0.2.0）：复用同一请求模型
```

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
| **`INotificationNotifier`** | 通道抽象（v0.1.0 仅 Inbox） | `InboxNotifier` |
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

## 五、依赖关系

```
TKWF.Ext.Notifications
├── 主框架 Domain + SG1（实体）
├── TKWF.Ext.Permissions.Abstractions（发布方权限门控 IPermissionChecker，ADR48 D7）
└── 事件总线（[DomainEventHandler] + ILocalEventHandler）
```

## 六、架构演进路线 (Architecture Roadmap)

### V0.1.0（当前）
- 站内通知核心：发布/收件箱/订阅/定义注册 + 事件驱动先例 + 通道抽象（inbox-only）
- Oracle 评审 PASS WITH CONDITIONS（C1-C5 修订 + m1-m7 处理）

### V0.2.0（候选，能力完善）
- **Email 通道**：先建 `TKWF.Ext.Emailing.Abstractions`（仅抽 `IEmailSender`+`EmailMessage`，C3），`EmailNotifier` 调 `IEmailSender`
- **SignalR 通道**：实时推送（`Clients.User(userId).SendAsync`，需评估 AspNetCore.SignalR）
- **用户偏好路由**：按用户偏好选择通道（UseChannels 用户级覆盖）
- **逐用户权限门控**：收件人级权限过滤（需 Permissions 批量 API）

### 远期 / 评估
- 通知本地化（`ILocalizableString`，对接 D16/ADR31）
- 通知模板渲染（复用 Emailing V0.2.0 TextTemplates / PrintTemplates）
- 批量派发优化（RecipientBatchSize 落地）
- **跨表查询 VEntity 化**（评估）：收件箱按通知名过滤当前用两步查询（投影取 Id 已优化）——若单名发布量达万级，改 `vw_UserNotificationWithNotification` VEntity（JOIN 单查询下推 DB，v4.9.102 方案：VEntity 不生成 DataService/REST 端点，需手写只读 Service 继承 `DomainReadOnlyDataServiceBase` 注入 `IEntityReadOnlyDAC<T>` + 生产建 SQL View）

**文档信息**: V0.1.0 | 2026-09-06 | 关联：v0.1.0-Notifications-通知中心-开发方案.md、ADR-Notifications-事件驱动接线模式.md、ADR-Notifications-数据模型三层选型.md（主框架私有）
