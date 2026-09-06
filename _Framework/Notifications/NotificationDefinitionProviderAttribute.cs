using System;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 标记类为通知定义贡献者——SG/运行时扫描注册（对齐 Navigation [MenuContributor] 先例，m6）。
/// <para>消费方实现 <see cref="INotificationDefinitionProvider"/> 并标注本特性，扩展初始化时收集定义。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotificationDefinitionProviderAttribute : Attribute
{
}