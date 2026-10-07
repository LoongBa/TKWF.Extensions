using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Federation.Alipay;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 支付宝开放平台平台网关库 DI 装配扩展（库无 Initializer——库提供扩展方法，消费方/装配层调用；
/// Federation 扩展不强制引库保持核心轻量）。
/// <para>注册内容：AlipayOptions 配置绑定 + AlipayApiClient（出站 typed client）+ <see cref="AlipaySignService"/>
/// （RSA2 双向签名/验签——纯逻辑单例）+ <see cref="AlipayChannelSource"/>（组件 8.5 多通道静态来源）+
/// ISsoChannel 通道（AlipayOauthChannel——TryAddEnumerableConstructible ADR92 多实现集合守卫工厂；
/// 支付宝无入站回调 → 仅 OAuth 通道，出站-only 形态）。</para>
/// <para>消费方用法：</para>
/// <code>
/// builder.Services.AddAlipayFederationChannels(x => { /* TKWF:Federation:Alipay 节已自动绑定，此处可编程覆盖 */ });
/// </code>
/// </summary>
public static class AlipayFederationServiceCollectionExtensions
{
    /// <summary>
    /// 装配支付宝开放平台网关（AlipayOptions + AlipayApiClient + AlipaySignService + ISsoChannel 通道）。
    /// <para><paramref name="configure"/>：编程式覆盖 AlipayOptions（优先于配置节；测试/动态装配用）。
    /// 默认 <c>null</c>——配置节 <c>TKWF:Federation:Alipay</c> 由 SG1 [Options] 在消费方自动绑定 + 此处 AddOptions 兜底。</para>
    /// <para>AlipayApiClient 注册：<c>AddHttpClient&lt;AlipayApiClient&gt;()</c>（typed client——ctor 收
    /// <see cref="System.Net.Http.HttpClient"/> + IOptions + AlipaySignService + ILogger，对齐
    /// Microsoft.Extensions.Http 惯例；DI 生命周期托管防 socket 悬挂）。
    /// ⚠️ 禁止 BuildServiceProvider 提前解析（临时根容器 + Scoped 捕获异常）——延迟应用配置。</para>
    /// </summary>
    public static IServiceCollection AddAlipayFederationChannels(
        this IServiceCollection services,
        Action<AlipayOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // AlipayOptions：SG1 [Options("TKWF:Federation:Alipay")] 消费方自动绑定 + AddOptions 兜底默认值 +
        // 编程式 configure 覆盖（测试/动态装配注入）
        services.AddOptions<AlipayOptions>().Configure(o => configure?.Invoke(o));

        // 出站客户端：HttpClient 经 typed client 注入（AddHttpClient<AlipayApiClient>——ctor 收 HttpClient）
        services.AddHttpClient<AlipayApiClient>();

        // RSA2 双向签名/验签（纯逻辑单例——ApiClient/通道共享）
        services.AddSingleton<AlipaySignService>();

        // 多通道联邦（v0.3.0）：静态通道来源——支付宝 Channels 投影为统一 ChannelConfig（StaticChannelRegistry 聚合选区）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChannelSource, AlipayChannelSource>());

        // ISsoChannel 多实现集合守卫工厂（ADR92）——仅 OAuth 通道（支付宝无入站回调，出站-only 形态）
        // （v0.3.0 多通道：集合元素 = 模板实例——SsoChannelFactory 按 ChannelType 匹配 + 按 channelId 按需构造）
        services.TryAddEnumerableConstructible<ISsoChannel, AlipayOauthChannel>();

        return services;
    }
}
