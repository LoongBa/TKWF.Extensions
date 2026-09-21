using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知定义——描述一个业务通知（稳定名 + 显示名 + 严重级别 + 可选发布方权限 + 可选投递通道）。
/// <para>由 <see cref="INotificationDefinitionProvider"/> 在启动时注册，发布前经
/// <see cref="INotificationDefinitionManager.Get"/> 校验存在性（快速失败）。</para>
/// <para><b>V0.5.0 本地化</b>：可选 <see cref="DisplayNameKey"/> 挂接主框架 ADR31 i18n
/// （<c>TKW.Framework.Localization.IFrameworkLocalizer</c>）——设置 key 后发布时按当前 culture 解析
/// 本地化显示名快照；未设置（null）则原样使用 <see cref="DisplayName"/> 文本（v0.4.0 及更早行为，零变化）。
/// key 约定：<c>Notifications/Definition_{Name}</c>（如 <c>Notifications/Definition_OrderShipped</c>）。</para>
/// </summary>
public sealed class NotificationDefinition
{
    /// <summary>创建通知定义（v0.4.0 及更早构造，零破坏）。</summary>
    /// <param name="name">稳定唯一名（如 "OrderShipped"）。</param>
    /// <param name="displayName">显示名。</param>
    /// <param name="severity">默认严重级别。</param>
    public NotificationDefinition(
        string name,
        string displayName,
        NotificationSeverity severity = NotificationSeverity.Info)
        : this(name, displayName, displayNameKey: null, severity)
    {
    }

    /// <summary>创建通知定义（V0.5.0 加性重载——可选本地化 key）。</summary>
    /// <param name="name">稳定唯一名（如 "OrderShipped"）。</param>
    /// <param name="displayName">显示名（本地化未命中/未启用时的回退文本）。</param>
    /// <param name="displayNameKey">本地化显示名 key（null = 原样使用 <paramref name="displayName"/>，
    /// 零变化）。约定 <c>Notifications/Definition_{Name}</c>。</param>
    /// <param name="severity">默认严重级别。</param>
    public NotificationDefinition(
        string name,
        string displayName,
        string? displayNameKey,
        NotificationSeverity severity = NotificationSeverity.Info)
    {
        Name = name ?? throw new System.ArgumentNullException(nameof(name));
        DisplayName = displayName ?? throw new System.ArgumentNullException(nameof(displayName));
        if (displayNameKey != null && string.IsNullOrWhiteSpace(displayNameKey))
            throw new System.ArgumentException("DisplayNameKey 不能为空白字符串", nameof(displayNameKey));
        DisplayNameKey = displayNameKey;
        Severity = severity;
    }

    /// <summary>通知名（稳定唯一）。</summary>
    public string Name { get; }

    /// <summary>显示名（V0.5.0：本地化回退文本——key 未命中/未启用时使用此值；既有行为零变化）。</summary>
    public string DisplayName { get; }

    /// <summary>本地化显示名 key（V0.5.0，null = 不本地化）。约定 <c>Notifications/Definition_{Name}</c>。</summary>
    public string? DisplayNameKey { get; }

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