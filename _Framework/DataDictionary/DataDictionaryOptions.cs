namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典配置选项。
    /// <para>通过 <c>services.Configure&lt;DataDictionaryOptions&gt;(config.GetSection("TKWF:DataDictionary"))</c> 绑定。</para>
    /// </summary>
    public class DataDictionaryOptions
    {
        /// <summary>是否启用数据字典（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;
    }
}