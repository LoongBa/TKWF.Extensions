namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典配置选项（V0.2.0 扩展缓存与树形分组）。
    /// <para>通过 <c>services.Configure&lt;DataDictionaryOptions&gt;(config.GetSection("TKWF:DataDictionary"))</c> 绑定。</para>
    /// </summary>
    public class DataDictionaryOptions
    {
        /// <summary>是否启用数据字典（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>是否启用按 Code 内存缓存（默认 true）。关闭后每次查库（调试/测试场景）。</summary>
        public bool EnableCache { get; set; } = true;

        /// <summary>缓存过期时间（秒，默认 300）。仅 <see cref="EnableCache"/> = true 时生效。</summary>
        public int CacheExpirationSeconds { get; set; } = 300;

        /// <summary>是否启用树形模式（默认 false）。启用后 <c>GetItemsTreeAsync</c> 返回嵌套树结构。</summary>
        public bool EnableTreeMode { get; set; }
    }
}
