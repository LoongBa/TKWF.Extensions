using System.Collections.Generic;

namespace TKWF.Ext.Notifications;

/// <summary>
/// 通知定义管理器——维护已注册的通知定义（启动时从 Provider 收集，不可变）。
/// <para>发布前经 <see cref="Get"/> 校验定义存在性（快速失败）。</para>
/// </summary>
public interface INotificationDefinitionManager
{
    /// <summary>获取定义（不存在 → <see cref="KeyNotFoundException"/>）。</summary>
    NotificationDefinition Get(string name);

    /// <summary>定义是否存在。</summary>
    bool Exists(string name);

    /// <summary>全部已注册定义。</summary>
    IReadOnlyList<NotificationDefinition> GetAll();
}