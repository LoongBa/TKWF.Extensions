using Microsoft.AspNetCore.SignalR;

namespace TKWF.Ext.Notifications.SignalR;

/// <summary>
/// 通知 Hub——<b>类型锚</b>（server-side push only 模式）。
/// <para>无业务方法：客户端连接后经 <c>connection.on(methodName, callback)</c> 订阅，
/// 服务端经 <see cref="IHubContext{THub}"/>.<c>Clients.User(userId).SendAsync(methodName, payload)</c>
/// 推送通知负载（Oracle P2-1 评审确认：server-side push only 是 SignalR 标准模式，Hub 无需声明方法）。</para>
/// <para>public 必须（跨程序集 <see cref="IHubContext{NotificationsHub}"/> 泛型锚点可见性——
/// <see cref="SignalRNotifier"/> 在扩展程序集引用本类型）。</para>
/// </summary>
public sealed class NotificationsHub : Hub
{
}