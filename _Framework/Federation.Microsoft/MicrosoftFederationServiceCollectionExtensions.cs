using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using TKW.Framework.Domain;
using TKWF.Federation.Oidc;

namespace TKWF.Federation.Microsoft;

/// <summary>
/// Microsoft 平台网关库 DI 装配扩展（纯库无 Initializer——库侧注册扩展方法）。
/// <para>注册：MicrosoftOptions 配置绑定（AddOptions().Configure——不 BuildServiceProvider）
/// + <c>AddOidcDerivedChannels&lt;MicrosoftOidcChannel&gt;()</c>（M1 基座——pairwise channel_id 复合编码入集合）。</para>
/// </summary>
public static class MicrosoftFederationServiceCollectionExtensions
{
    /// <summary>注册 Microsoft OIDC 通道（MicrosoftOidcChannel——pairwise sub 复合编码 microsoft_oidc:{client_id}）。</summary>
    public static IServiceCollection AddMicrosoftFederationChannels(
        this IServiceCollection services, Action<MicrosoftOptions>? configure = null)
    {
        services.AddOptions<MicrosoftOptions>().Configure(o => configure?.Invoke(o));
        services.AddOidcDerivedChannels<MicrosoftOidcChannel>();   // 基座注册 OidcChannelFlow + 通道入集合
        return services;
    }
}