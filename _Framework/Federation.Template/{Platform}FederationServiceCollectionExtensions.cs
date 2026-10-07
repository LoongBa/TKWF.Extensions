using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;

namespace TKWF.Federation.{Namespace};

/// <summary>
/// {平台}平台网关库 DI 装配扩展（库无 Initializer——库提供扩展方法，消费方/装配层调用；
/// Federation 扩展不强制引库保持核心轻量）。
/// <para>注册内容：{Platform}Options 配置绑定 + {Platform}ApiClient（出站）+ ISsoChannel 通道
/// （{Platform}OauthChannel{有入站回调平台追加：/{Platform}EventChannel}——TryAddEnumerableConstructible ADR92
/// 多实现集合守卫工厂）。</para>
/// <para>消费方用法：</para>
/// <code>
/// builder.Services.Add{Platform}FederationChannels(x => { /* TKWF:Federation:{Namespace} 节已自动绑定，此处可编程覆盖 */ });
/// </code>
/// </summary>
public static class {Platform}FederationServiceCollectionExtensions
{
    /// <summary>
    /// 装配{平台}平台网关（{Platform}Options + {Platform}ApiClient + ISsoChannel channels）。
    /// <para><paramref name="configure"/>：编程式覆盖 {Platform}Options（优先于配置节；测试/动态装配用）。
    /// 默认 <c>null</c>——配置节 <c>TKWF:Federation:{Namespace}</c> 由 SG1 [Options] 在消费方自动绑定 + 此处 AddOptions 兜底。</para>
    /// <para>{Platform}ApiClient 注册：<c>AddHttpClient&lt;{Platform}ApiClient&gt;()</c>（typed client——ctor 收
    /// <see cref="HttpClient"/> + IOptions + ILogger，对齐 Microsoft.Extensions.Http 惯例；DI 生命周期托管防 socket 悬挂）。</para>
    /// </summary>
    public static IServiceCollection Add{Platform}FederationChannels(
        this IServiceCollection services,
        Action<{Platform}Options>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // {Platform}Options：SG1 [Options("TKWF:Federation:{Namespace}")] 消费方自动绑定 + AddOptions 兜底默认值 +
        // 编程式 configure 覆盖（测试/装配动态注入）。
        // ⚠️ 禁止 BuildServiceProvider 提前解析（临时根容器 + Scoped 捕获异常）——延迟应用配置
        services.AddOptions<{Platform}Options>().Configure(o => configure?.Invoke(o));

        // 出站客户端：HttpClient 经 typed client 注入（AddHttpClient<{Platform}ApiClient>——ctor 收 HttpClient）
        services.AddHttpClient<{Platform}ApiClient>();

        // ISsoChannel 多实现集合守卫工厂（ADR92）——OAuth 通道{有入站回调平台追加： + 事件通道}
        services.TryAddEnumerableConstructible<ISsoChannel, {Platform}OauthChannel>();
        // 有入站回调平台追加：
        // services.TryAddEnumerableConstructible<ISsoChannel, {Platform}EventChannel>();

        // 多通道联邦（v0.3.0）：静态通道来源——{Platform} Channels 投影为统一 ChannelConfig（StaticChannelRegistry 聚合选区）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChannelSource, {Platform}ChannelSource>());

        return services;
    }
}
