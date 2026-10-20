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
/// （WeChatOauthChannel/WeChatEventChannel——TryAddEnumerableConstructible ADR92 多实现集合守卫工厂）
/// + 平台层 EventReply Options（<see cref="WechatEventReplyOptions"/> 探针开关）+
/// 探针 handler（<see cref="WechatProbeEventReplyHandler"/> 无条件注册，IOptions 门控）。
/// ⚠️ Template handler（<see cref="WechatTemplateEventReplyHandler"/>）<b>不默认注册</b>
/// （Oracle P0-1——平台库不注册 handler 实例防过度设计，仅 Probe 因开关门控无条件注册；Template 归消费方显式注册）。</para>
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
        // 编程式 configure 覆盖（测试/动态装配注入）
        services.AddOptions<WeChatOptions>().Configure(o => configure?.Invoke(o));

        // 出站客户端：HttpClient 经 typed client 注入（AddHttpClient<WeChatApiClient>——ctor 收 HttpClient
        //   + IWeChatCredentialResolver + ILogger；resolver 非空——DI 容器注入）
        services.AddHttpClient<WeChatApiClient>();

        // 凭证解析可插拔契约（V0.4.1——Oracle P1：DI 注册缺省 Options 实现，ctor 非空不做 ctor 内 fallback；
        //   OptionsWeChatCredentialResolver 读 TKWF:Federation:WeChat 节 Channels 按 AppId 精确匹配——
        //   现状 ResolveCredential 语义迁移，Oracle P2-4 凭证自持；扩展可 TryAddEnumerable 注册 DB/KMS 实现
        //   （DB 命中优先，未命中委托缺省 Options 实现——两源并存，方案 WeChatOperations §3.3.4））
        services.TryAddSingleton<IWeChatCredentialResolver, OptionsWeChatCredentialResolver>();


        // 多通道联邦（v0.3.0）：静态通道来源——微信 Channels 投影为统一 ChannelConfig（StaticChannelRegistry 聚合选区）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChannelSource, WeChatChannelSource>());

// ISsoChannel 多实现集合守卫工厂（ADR92）——OAuth 通道 + 事件通道
        // （v0.3.0 多通道：集合元素 = 模板实例——SsoChannelFactory 按 ChannelType 匹配具体类型 + 按 channelId 按需构造）
        services.TryAddEnumerableConstructible<ISsoChannel, WeChatOauthChannel>();
        services.TryAddEnumerableConstructible<ISsoChannel, WeChatEventChannel>();

        // 平台层 EventReply Options（TKWF:Federation:WeChat:EventReply 节——探针开关，方案 §3.5；
        //   SG1 [Options] 消费方自动绑定 + 此处 AddOptions 兜底默认值，对齐 WeChatOptions 双通道模式）
        services.AddOptions<WechatEventReplyOptions>();

        // 探针 handler：无条件注册（Oracle P1-3——TryAddEnumerableConstructible），内部
        //   IOptions<WechatEventReplyOptions> 惰性判断 ProbeEventReplyHandlerEnabled：
        //   关（默认 false，P1-1 fail-safe）→ 全分支 null（不回复不干预）；开 → PROBE 可区分文本。
        // ⚠️ Template handler 不在此注册（Oracle P0-1——平台库不注册 handler 实例防过度设计）：
        //   消费方显式 services.TryAddEnumerableConstructible<IEventReplyHandler, WechatTemplateEventReplyHandler>();
        services.TryAddEnumerableConstructible<IEventReplyHandler, WechatProbeEventReplyHandler>();

        return services;
    }
}