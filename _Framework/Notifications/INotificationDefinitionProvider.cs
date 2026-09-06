namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知定义注册上下文——provider 在 <see cref="INotificationDefinitionProvider.Define"/> 内添加定义。
/// </summary>
public interface INotificationDefinitionContext
{
    /// <summary>注册一个通知定义。</summary>
    void Add(NotificationDefinition definition);
}

/// <summary>
/// 通知定义贡献者——业务模块注册自己的通知定义。
/// <para>实现类标注 <see cref="NotificationDefinitionProviderAttribute"/> 由扩展发现（运行时扫描，m6）。</para>
/// </summary>
public interface INotificationDefinitionProvider
{
    /// <summary>注册通知定义。</summary>
    void Define(INotificationDefinitionContext context);
}