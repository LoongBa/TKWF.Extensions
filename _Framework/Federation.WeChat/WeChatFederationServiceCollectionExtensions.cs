using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信公众平台网关库 DI 装配扩展（库无 Initializer——Oracle 评审点 7 裁定：库提供扩展方法，
/// 消费方/装配层调用；Federation 扩展不强制引库保持核心轻量）。
/// <para>注册内容：WeChatOptions 配置绑定 + WeChatApiClient（出站）+ 两 ISsoChannel 通道
/// （WeChatOauthChannel/WeChatEventChannel——TryAddEnumerableConstructible ADR92 多实现集合守卫工厂）。</para>
/// <para>消费方用法：</para>
/// <code>
/// builder.Services.AddWeChatFederationChannels(x => { /* TKWF:Federation:WeChat 节已自动绑定，此处可编程覆盖 */ });
/// </code>
/// </summary>
public static class WeChatFederationServiceCollectionExtensions
{
    /// <summary>
    /// 装配微信公众平台网关（WeChatOptions + WeChatApiClient + two ISsoChannel channels）。
    /// <para><paramref name="configure"/>：编程式覆盖 WeChatOptions（优先于配置节；测试/动态装配用）。
    /// 默认 <c>null</c>——配置节 <c>TKWF:Federation:WeChat</c> 由 SG1 [Options] 在消费方自动绑定 + 此处 AddOptions 兜底。</para>
    /// <para>WeChatApiClient 注册：<c>AddHttpClient&lt;WeChatApiClient&gt;()</c>（typed client——ctor 收
    /// <see cref="HttpClient"/> + IOptions + ILogger，对齐 Microsoft.Extensions.Http 惯例；DI 生命周期托管防 socket 悬挂）。</para>
    /// </summary>
    public static IServiceCollection AddWeChatFederationChannels(
        this IServiceCollection services,
        Action<WeChatOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // WeChatOptions：SG1 [Options("TKWF:Federation:WeChat")] 消费方自动绑定 + AddOptions 兜底默认值 +
        // 编程式 configure 覆盖（测试/装配动态注入）
        services.AddOptions<WeChatOptions>().Configure(o => configure?.Invoke(o));

        // 出站客户端：HttpClient 经 typed client 注入（AddHttpClient<WeChatApiClient>——ctor 收 HttpClient）
        services.AddHttpClient<WeChatApiClient>();

        // ISsoChannel 多实现集合守卫工厂（ADR92）——OAuth 通道 + 事件通道
        services.TryAddEnumerableConstructible<ISsoChannel, WeChatOauthChannel>();
        services.TryAddEnumerableConstructible<ISsoChannel, WeChatEventChannel>();

        return services;
    }
}