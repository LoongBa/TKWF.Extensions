using System.Collections.Generic;

namespace TKWF.Ext.FeatureManagement;

/// <summary>功能管理配置（<c>TKWF:FeatureManagement</c> 节）。</summary>
public class FeatureOptions
{
    /// <summary>Feature 值缓存过期秒数（IMemoryCache——进程内，多实例部署跨实例至多此 TTL 收敛）。</summary>
    public int CacheExpirationSeconds { get; set; } = 300;

    /// <summary>
    /// Provider 解析顺序（v0.2.0——首个命中返回，对齐 ABP FeatureOptions.Providers）。
    /// 默认 = v0.1.0 四层顺序（User→Role→Tenant→Global）；未列入的自定义 Provider 追加末尾；
    /// 空列表 = 全部注册 Provider 按注册顺序。
    /// </summary>
    public List<string> ProviderOrder { get; set; } = [FeatureProviders.User, FeatureProviders.Role, FeatureProviders.Tenant, FeatureProviders.Global];
}
