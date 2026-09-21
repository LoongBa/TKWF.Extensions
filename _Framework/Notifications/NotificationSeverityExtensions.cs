using TKW.Framework.Localization;

namespace TKWF.Ext.Notifications;

/// <summary>
/// <see cref="NotificationSeverity"/> 本地化显示名扩展（V0.5.0——对接主框架 ADR31 i18n）。
/// <para>key 约定：<c>Notifications/Severity_{Value}</c>（如 <c>Notifications/Severity_Warning</c>）；
/// 消费方经 <c>AddFrameworkLocalization(o =&gt; o.Contributors.Add(...))</c> 贡献译文。
/// 解析规则：localizer 命中返回译文；null（未注册）/未命中（返回 key 本身）/空串 → 回退英文枚举名。</para>
/// </summary>
public static class NotificationSeverityExtensions
{
    /// <summary>
    /// 本地化显示名——localizer 命中返回译文；null/未命中/空串回退英文枚举名（Info/Success/Warning/Error）。
    /// <para>客户端 enum→文案映射约定不受影响：未启用本地化时行为与 v0.4.0 一致（枚举名）。</para>
    /// </summary>
    public static string GetDisplayName(this NotificationSeverity severity, IFrameworkLocalizer? localizer)
    {
        if (localizer == null)
            return severity.ToString();

        var key = $"Notifications/Severity_{severity}";
        var resolved = localizer[key];
        return string.IsNullOrWhiteSpace(resolved) || resolved == key
            ? severity.ToString()
            : resolved;
    }
}