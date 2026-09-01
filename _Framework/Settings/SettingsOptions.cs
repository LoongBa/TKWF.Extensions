namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置管理配置选项。
    /// <para>通过 <c>services.Configure&lt;SettingsOptions&gt;(config.GetSection("TKWF:Settings"))</c> 绑定。</para>
    /// </summary>
    public class SettingsOptions
    {
        /// <summary>默认设置值提供者名称（默认 "Global"）。</summary>
        public string DefaultSettingValueProvider { get; set; } = "Global";

        /// <summary>是否启用设置管理（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;
    }
}
