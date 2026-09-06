namespace TKWF.Ext.Notifications;

/// <summary>
/// Notifications 扩展配置——绑定 TKWF:Notifications 配置节。
/// </summary>
public sealed class NotificationsOptions
{
    /// <summary>订阅者派发批处理大小（v0.1.0 仅文档化，v0.2.0 批量派发使用）。</summary>
    public int RecipientBatchSize { get; set; } = 256;
}