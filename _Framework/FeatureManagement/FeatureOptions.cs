namespace TKWF.Ext.FeatureManagement;

/// <summary>功能管理配置（<c>TKWF:FeatureManagement</c> 节）。</summary>
public class FeatureOptions
{
    /// <summary>Feature 值缓存过期秒数（IMemoryCache——进程内，多实例部署跨实例至多此 TTL 收敛）。</summary>
    public int CacheExpirationSeconds { get; set; } = 300;
}
