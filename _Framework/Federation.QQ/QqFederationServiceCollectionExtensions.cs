using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;

namespace TKWF.Federation.QQ;

/// <summary>
/// QQ 互联平台网关库 DI 装配扩展（库无 Initializer——库提供扩展方法，消费方/装配层调用；
/// Federation 扩展不强制引库保持核心轻量）。
/// <para>注册内容：QqOptions 配置绑定 + QqApiClient（出站）+ ISsoChannel 通道
/// （QqOauthChannel——TryAddEnumerableConstructible ADR92 多实现集合守卫工厂；QQ 无入站回调 → 仅 OAuth 通道，出站-only 形态）。</para>
/// <para>消费方用法：</para>
/// <code>
/// builder.Services.AddQqFederationChannels(x => { /* TKWF:Federation:QQ 节已自动绑定，此处可编程覆盖 */ });
/// </code>
/// <para>⚠️ N1 骨架——注册壳已可运行（通道骨骼入集合）；协议实现在 N3 立项填充。</para>
/// </summary>
public static class QqFederationServiceCollectionExtensions
{
    /// <summary>
    /// 装配 QQ 互联平台网关（QqOptions + QqApiClient + ISsoChannel 通道）。
    /// <para><paramref name="configure"/>：编程式覆盖 QqOptions（优先于配置节；测试/动态装配用）。
    /// 默认 <c>null</c>——配置节 <c>TKWF:Federation:QQ</c> 由 SG1 [Options] 在消费方自动绑定 + 此处 AddOptions 兜底。</para>
    /// <para>QqApiClient 注册：<c>AddHttpClient&lt;QqApiClient&gt;()</c>（typed client——ctor 收 <see cref="HttpClient"/>
    /// + IOptions + ILogger，对齐 Microsoft.Extensions.Http 惯例；DI 生命周期托管防 socket 悬挂）。</para>
    /// </summary>
    public static IServiceCollection AddQqFederationChannels(
        this IServiceCollection services,
        Action<QqOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // QqOptions：SG1 [Options("TKWF:Federation:QQ")] 消费方自动绑定 + AddOptions 兜底默认值 +
        // 编程式 configure 覆盖（测试/装配动态注入）。
        // ⚠️ 禁止 BuildServiceProvider 提前解析（临时根容器 + Scoped 捕获异常）——延迟应用配置
        services.AddOptions<QqOptions>().Configure(o => configure?.Invoke(o));

        // 出站客户端：HttpClient 经 typed client 注入（AddHttpClient<QqApiClient>——ctor 收 HttpClient）
        services.AddHttpClient<QqApiClient>();

        // ISsoChannel 多实现集合守卫工厂（ADR92）——仅 OAuth 通道（QQ 无入站回调，出站-only 形态）
        services.TryAddEnumerableConstructible<ISsoChannel, QqOauthChannel>();

        return services;
    }
}