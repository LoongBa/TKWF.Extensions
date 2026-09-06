namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知定义——描述一个业务通知（稳定名 + 显示名 + 严重级别 + 可选发布方权限）。
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
}