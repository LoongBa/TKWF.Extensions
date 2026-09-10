using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知定义——描述一个业务通知（稳定名 + 显示名 + 严重级别 + 可选发布方权限 + 可选投递通道）。
/// <para>由 <see cref="INotificationDefinitionProvider"/> 在启动时注册，发布前经
/// <see cref="INotificationDefinitionManager.Get"/> 校验存在性（快速失败）。</para>
/// </summary>
public sealed class NotificationDefinition
{
    /// <summary>创建通知定义。</summary>
    /// <param name="name">稳定唯一名（如 "OrderShipped"）。</param>
    /// <param name="displayName">显示名。</param>
    /// <param name="severity">默认严重级别。</param>
    public NotificationDefinition(
        string name,
        string displayName,
        NotificationSeverity severity = NotificationSeverity.Info)
    {
        Name = name ?? throw new System.ArgumentNullException(nameof(name));
        DisplayName = displayName ?? throw new System.ArgumentNullException(nameof(displayName));
        Severity = severity;
    }

    /// <summary>通知名（稳定唯一）。</summary>
    public string Name { get; }

    /// <summary>显示名。</summary>
    public string DisplayName { get; }

    /// <summary>默认严重级别。</summary>
    public NotificationSeverity Severity { get; }

    /// <summary>发布方必需权限（null=不校验）。C1 修订：仅校验发布方（当前用户），非逐收件人。</summary>
    public string? PermissionName { get; private set; }

    /// <summary>设置发布方必需权限（Fluent API）。</summary>
    public NotificationDefinition RequirePermission(string permissionName)
    {
        PermissionName = permissionName ?? throw new System.ArgumentNullException(nameof(permissionName));
        return this;
    }

    /// <summary>
    /// 投递通道（默认 ["Inbox"]）——发布时按此声明顺序逐通道投递（v0.2.0 多通道路由）。
    /// <para>经 <see cref="NotificationPublisher"/> 逐通道匹配 <see cref="INotificationNotifier.Name"/> 投递；
    /// 未声明通道名的 notifier 自然跳过。不存在的通道名不校验（投递时无匹配 notifier 即跳过，不抛异常）。</para>
    /// </summary>
    public IReadOnlyList<string> Channels { get; private set; } = new[] { "Inbox" };

    /// <summary>设置投递通道（Fluent API）——按给定顺序去重存储。</summary>
    /// <param name="channels">通道名（如 "Inbox"/"Email"/"SignalR"）。传空数组抛
    /// <see cref="System.ArgumentException"/>。</param>
    public NotificationDefinition UseChannels(params string[] channels)
    {
        if (channels is null || channels.Length == 0)
            throw new System.ArgumentException("UseChannels 至少需要一个通道", nameof(channels));
        Channels = channels.Distinct().ToArray();
        return this;
    }
}