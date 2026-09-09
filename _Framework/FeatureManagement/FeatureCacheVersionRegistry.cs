using System;
using System.Collections.Concurrent;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// Feature 缓存版本表（public sealed，Singleton）——v0.2.0 版本号缓存 key 方案核心
/// （评审 C3/C5 裁定，替代 v0.1.0 登记表）：
/// <para>缓存 key = <c>Feature:{name}:v{version}:{providerName}:{providerKey}</c>——version 嵌入；
/// 写后 <see cref="BumpVersion"/>（version++）→ 新 key 全部 miss——User/Role/Tenant/Global 全层天然失效
/// （无需登记枚举动态 ProviderKey）；旧 key 由 CacheExpirationSeconds TTL 清理（无泄漏）。</para>
/// <para>public（v0.3.0）：跨实例失效 handler <see cref="DistributedFeatureChangedHandler"/> 的公开构造函数
/// 需要可访问的依赖类型（DI 解析要求公开构造函数；internal 参数类型触发 CS0051）——C1 裁定 handler 必须 public
/// 的附带要求。版本表语义/线程安全不变：<see cref="ConcurrentDictionary{TKey,TValue}"/> 原子操作（无遍历-修改竞争，C5）。</para>
/// </summary>
public sealed class FeatureCacheVersionRegistry
{
    private readonly ConcurrentDictionary<string, long> _versions = new(StringComparer.Ordinal);

    /// <summary>当前版本号（默认 0）。</summary>
    public long GetVersion(string name) => _versions.GetOrAdd(name, 0);

    /// <summary>写后版本递增（全层失效）——原子自增。</summary>
    public long BumpVersion(string name) => _versions.AddOrUpdate(name, 1, (_, v) => v + 1);
}
