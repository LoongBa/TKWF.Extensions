using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.DingTalk;

/// <summary>
/// 钉钉开放平台网关库 DI 装配扩展（库无 Initializer——Oracle 评审点 7 裁定：库提供扩展方法，
/// 消费方/装配层调用；Federation 扩展不强制引库保持核心轻量）。
/// <para>注册内容：DingTalkOptions 配置绑定 + DingTalkApiClient（出站）+ 两 ISsoChannel 通道
/// （DingTalkOauthChannel/DingTalkEventChannel——TryAddEnumerableConstructible ADR92 多实现集合守卫工厂）。</para>
/// <para>消费方用法：</para>
/// <code>
/// builder.Services.AddDingTalkFederationChannels(x => { /* TKWF:Federation:DingTalk 节已自动绑定，此处可编程覆盖 */ });
/// </code>
/// </summary>
public static class DingTalkFederationServiceCollectionExtensions
{
    /// <summary>
    /// 装配钉钉开放平台网关（DingTalkOptions + DingTalkApiClient + two ISsoChannel channels）。
    /// <para><paramref name="configure"/>：编程式覆盖 DingTalkOptions（优先于配置节；测试/动态装配用）。
    /// 默认 <c>null</c>——配置节 <c>TKWF:Federation:DingTalk</c> 由 SG1 [Options] 在消费方自动绑定 + 此处 AddOptions 兜底。</para>
    /// <para>DingTalkApiClient 注册：<c>AddHttpClient&lt;DingTalkApiClient&gt;()</c>（typed client——ctor 收
    /// <see cref="System.Net.Http.HttpClient"/> + IOptions + ILogger，对齐 Microsoft.Extensions.Http 惯例；DI 生命周期托管防 socket 悬挂）。</para>
    /// </summary>
    public static IServiceCollection AddDingTalkFederationChannels(
        this IServiceCollection services,
        Action<DingTalkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // DingTalkOptions：SG1 [Options("TKWF:Federation:DingTalk")] 消费方自动绑定 + AddOptions 兜底默认值 +
        // 编程式 configure 覆盖（测试/装配动态注入）
        services.AddOptions<DingTalkOptions>().Configure(o => configure?.Invoke(o));

        // 出站客户端：HttpClient 经 typed client 注入（AddHttpClient<DingTalkApiClient>——ctor 收 HttpClient）
        services.AddHttpClient<DingTalkApiClient>();

        // 多通道联邦（v0.3.0）：静态通道来源——钉钉 Channels 投影为统一 ChannelConfig（StaticChannelRegistry 聚合选区）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChannelSource, DingTalkChannelSource>());

        // ISsoChannel 多实现集合守卫工厂（ADR92）——OAuth 通道 + 事件通道
        services.TryAddEnumerableConstructible<ISsoChannel, DingTalkOauthChannel>();
        services.TryAddEnumerableConstructible<ISsoChannel, DingTalkEventChannel>();

        return services;
    }
}
