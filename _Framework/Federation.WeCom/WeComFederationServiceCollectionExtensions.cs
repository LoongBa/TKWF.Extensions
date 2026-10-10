using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.WeCom;

/// <summary>
/// 企业微信网关库 DI 装配扩展（库无 Initializer——Oracle 评审点 7 裁定：库提供扩展方法，
/// 消费方/装配层调用；Federation 扩展不强制引库保持核心轻量）。
/// <para>注册内容：WeComOptions 配置绑定 + WeComApiClient（出站）+ 两 ISsoChannel 通道
/// （WeComOauthChannel/WeComEventChannel——TryAddEnumerableConstructible ADR92 多实现集合守卫工厂）。</para>
/// <para>消费方用法：</para>
/// <code>
/// builder.Services.AddWeComFederationChannels(x => { /* TKWF:Federation:WeCom 节已自动绑定，此处可编程覆盖 */ });
/// </code>
/// </summary>
public static class WeComFederationServiceCollectionExtensions
{
    /// <summary>
    /// 装配企业微信网关（WeComOptions + WeComApiClient + two ISsoChannel channels）。
    /// <para><paramref name="configure"/>：编程式覆盖 WeComOptions（优先于配置节；测试/动态装配用）。
    /// 默认 <c>null</c>——配置节 <c>TKWF:Federation:WeCom</c> 由 SG1 [Options] 在消费方自动绑定 + 此处 AddOptions 兜底。</para>
    /// <para>WeComApiClient 注册：<c>AddHttpClient&lt;WeComApiClient&gt;()</c>（typed client——ctor 收
    /// <see cref="System.Net.Http.HttpClient"/> + ILogger，对齐 Microsoft.Extensions.Http 惯例；DI 生命周期托管防 socket 悬挂）。</para>
    /// </summary>
    public static IServiceCollection AddWeComFederationChannels(
        this IServiceCollection services,
        Action<WeComOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // WeComOptions：SG1 [Options("TKWF:Federation:WeCom")] 消费方自动绑定 + AddOptions 兜底默认值 +
        // 编程式 configure 覆盖（测试/装配动态注入）
        services.AddOptions<WeComOptions>().Configure(o => configure?.Invoke(o));

        // 出站客户端：HttpClient 经 typed client 注入（AddHttpClient<WeComApiClient>——ctor 收 HttpClient）
        services.AddHttpClient<WeComApiClient>();

        // 多通道联邦（v0.3.0）：静态通道来源——企业微信 Channels 投影为统一 ChannelConfig（StaticChannelRegistry 聚合选区）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChannelSource, WeComChannelSource>());

        // ISsoChannel 多实现集合守卫工厂（ADR92）——OAuth 通道 + 事件通道
        // （v0.3.0 多通道：集合元素 = 模板实例——SsoChannelFactory 按 ChannelType 匹配具体类型 + 按 channelId 按需构造）
        services.TryAddEnumerableConstructible<ISsoChannel, WeComOauthChannel>();
        services.TryAddEnumerableConstructible<ISsoChannel, WeComEventChannel>();

        return services;
    }
}
