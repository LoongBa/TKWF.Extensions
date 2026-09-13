using System;

namespace TKWF.Ext.Notifications.SignalR;

/// <summary>
/// SignalR 推送负载记录——通知发布后推送给在线用户的精简 DTO。
/// <para>不含 UserId（连接已按 <c>Clients.User(userId)</c> 分组，客户端经连接自带身份）；</para>
/// <para>不含实体关联信息（EntityTypeName/EntityId）——实时推送仅即时提醒语义，
/// 详情/历史查询走 Inbox（持久化收件箱是真相源）。</para>
/// </summary>
public sealed record SignalRNotificationPayload(
    long NotificationId,
    string Name,
    string Severity,
    string? DataJson,
    DateTime CreateTime);