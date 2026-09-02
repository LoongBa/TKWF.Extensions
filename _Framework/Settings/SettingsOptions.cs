using TKW.Framework.Domain;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置管理配置选项——经 <see cref="OptionsAttribute"/> 声明配置节，SG1 生成绑定（消费方启动期自动
    /// <c>services.Configure&lt;SettingsOptions&gt;(configuration.GetSection("TKWF:Settings"))</c>，
    /// 与 Navigation/Permissions 扩展同模式）。
    /// <para>配置节：<c>TKWF:Settings</c>。消费方亦可在 <c>ConfigureExtensions</c> 中
    /// <c>services.Configure&lt;SettingsOptions&gt;(o =&gt; ...)</c> 覆盖。</para>
    /// </summary>
    [Options("TKWF:Settings")]
    public class SettingsOptions
    {
        /// <summary>默认设置值提供者名称（默认 "Global"）。</summary>
        public string DefaultSettingValueProvider { get; set; } = "Global";

        /// <summary>是否启用设置管理（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>缓存过期时间（秒），默认 300 秒（5 分钟）。</summary>
        public int CacheExpirationSeconds { get; set; } = 300;
    }
}
