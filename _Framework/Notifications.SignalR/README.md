# TKWF.Ext.Notifications.SignalR 通知中心 SignalR 实时推送通道

**状态**: 独立扩展包（V0.4.0 自 Notifications 拆包——ADR-Notifications-SignalR通道） | **版本**: V0.2.0 | **框架**: .NET 10 | **依赖**: `TKWF.Ext.Notifications`（通道契约 `INotificationNotifier`/`NotificationDeliveryRequest`）+ `TKWF.Domain.Web`（IWebExtension 装配钩子）+ **FrameworkReference `Microsoft.AspNetCore.App`**（SignalR 类型共享框架——零 NuGet 传输依赖，版本随 .NET）

## 定位

通知中心（Notifications）的 **SignalR 实时推送通道**（服务端非 UI——前端归消费方）：`NotificationsHub` 类型锚 + `SignalRNotifier` best-effort 推送（多实例集合，与主包 Inbox/Email 通道共存）+ `NotificationsHubWebExtension` Web 装配钩子（D6 边界内聚 AddSignalR + MapHub）。消费方按需引入本包（对齐 BackgroundJobs.Quartz 拆包先例）。

## 核心约束

- **服务端非 UI**：只提供 Hub + Notifier + 端点映射；前端 SignalR 客户端接线归消费方
- **best-effort 推送（M1）**：`IHubContext` 未注册（消费方漏 `AddSignalR()`）→ LogWarning 跳过不失败；`SendAsync` 失败同样不重抛阻塞发布流程（延迟可空解析 C1 模式）
- **多实例集合**：`TryAddEnumerable(ServiceDescriptor.Scoped<INotificationNotifier, SignalRNotifier>())`——与主包通道 `TryAddEnumerableConstructible` 共存（ADR92）
- **Web 装配钩子（v4.10.45）**：`IWebExtension` 宿主注册（AddSignalR D6 内聚 + MapHub），`AllowAnonymous` false 默认认证；旧 `MapTkfwNotificationsHub` 删除

## 内容

| 类型 | 说明 |
|------|------|
| `NotificationsHub` | SignalR Hub 类型锚（空 Hub——用户标识契约：连接经认证上下文，`UserIdentifierProvider` 归消费方） |
| `SignalRNotifier : INotificationNotifier` | 通道实现——`DeliverAsync` 延迟解析 `IHubContext<NotificationsHub>`，发送 `SignalRNotificationPayload` 到 `MethodName` 指定客户端方法（默认 `notificationReceived`）；失败 LogWarning 不抛 |
| `SignalRNotificationPayload` | 推送载荷——`NotificationId`/`Name`/`Severity`/`DataJson`/`CreateTime`（wire record） |
| `NotificationsSignalROptions` | 配置（`TKWF:Notifications:SignalR` 节）——`MethodName`（默认 `notificationReceived`）/ `Path`（默认 `/hubs/notifications`）/ `AllowAnonymous`（默认 false） |
| `NotificationsSignalRExtensionInitializer<TUserInfo>` | 初始izer（`[TKWFExtension("Notifications.SignalR")]`）——Options 绑定 + `SignalRNotifier` Scoped 集合注册 |
| `NotificationsHubWebExtension : IWebExtension` | Web 装配——`ConfigureServices` AddSignalR + Options 绑定；`DeclareEndpoints` MapHub（`requireAuthorization: !AllowAnonymous`） |

## 消费方式

```csharp
// 1. 引用 + 白名单（消费方领域初始化器）
[TKWFEnabledExtension(typeof(NotificationsSignalRExtensionInitializer<>))]

// 2. Web 装配（消费方 UseWebExtensions 处）——NotificationsHubWebExtension 一次声明
app.UseWebExtensions(e => e.Add<NotificationsHubWebExtension>());   // AddSignalR（幂等）+ Options + MapHub 全部内聚
```

配置节（可选）：

```json
{
  "TKWF": {
    "Notifications": {
      "SignalR": { "MethodName": "notificationReceived", "Path": "/hubs/notifications", "AllowAnonymous": false }
    }
  }
}
```

## 版本与发布

| 版本 | 内容 |
|------|------|
| V0.1.x | 拆包首发（V0.4.0 主包联动）——Hub 类型锚 + SignalRNotifier best-effort + 端点映射；FrameworkReference 共享框架零 NuGet |
| **V0.2.0** | Web 装配钩子（v4.10.45）——`NotificationsHubWebExtension`（AddSignalR D6 内聚 + MapHub），旧 `MapTkfwNotificationsHub` 删除；`AllowAnonymous` 配置化 |

- MinVerTagPrefix：`Notifications.SignalR/v`（独立 tag 独立演进）；当前 tag `Notifications.SignalR/v0.2.0`
- 详细用法见 [通知中心使用指南](../docs/Notifications/通知中心扩展-使用指南.md)（本通道并入主包指南）

<!-- EOF -->