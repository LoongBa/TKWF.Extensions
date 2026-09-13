namespace TKWF.Ext.Notifications.SignalR;

/// <summary>
/// Notifications SignalR 通道配置——绑定 <c>TKWF:Notifications:SignalR</c> 配置节。
/// <para>P2-6（Oracle）：接线型扩展无 SG1 实体——不标注 <c>[Options]</c> 特性、不依赖 SG1 自动绑定路径；
/// 由 <see cref="NotificationsSignalRExtensionInitializer{TUserInfo}.ConfigureServices"/> 内
/// <c>AddOptions().BindConfiguration("TKWF:Notifications:SignalR")</c> 显式绑定
/// （对齐主包 <c>NotificationsOptions</c> 现有模式）。</para>
/// </summary>
public sealed class NotificationsSignalROptions
{
    /// <summary>客户端订阅的 Hub 方法名（客户端 <c>connection.on(methodName, ...)</c>）。</summary>
    public string MethodName { get; set; } = "notificationReceived";

    /// <summary>推荐端点路径（<see cref="SignalREndpointExtensions.MapTkfwNotificationsHub"/> 默认值）。</summary>
    public string Path { get; set; } = "/hubs/notifications";

    /// <summary>端点认证豁免（默认 false——通知负载敏感须登录；true 时追加 AllowAnonymous 元数据）。</summary>
    public bool AllowAnonymous { get; set; } = false;
}