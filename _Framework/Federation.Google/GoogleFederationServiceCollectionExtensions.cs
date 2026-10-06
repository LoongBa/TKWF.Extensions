using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using TKW.Framework.Domain;
using TKWF.Federation.Oidc;

namespace TKWF.Federation.Google;

/// <summary>
/// Google 平台网关库 DI 装配扩展（纯库无 Initializer——库侧注册扩展方法，消费方/装配层调用）。
/// <para>注册：GoogleOptions 配置绑定（AddOptions().Configure——N1 模板 P1-1 修正：不 BuildServiceProvider）
/// + <c>AddOidcDerivedChannels&lt;GoogleOidcChannel&gt;()</c>（M1 基座——注册 OidcAuthFlow/JwksManager/验签器
/// + GoogleOidcChannel 入 <c>IEnumerable&lt;ISsoChannel&gt;</c> 集合）。</para>
/// </summary>
public static class GoogleFederationServiceCollectionExtensions
{
    /// <summary>注册 Google OIDC 通道（GoogleOidcChannel——public sub 通配 google_oidc:*）。</summary>
    public static IServiceCollection AddGoogleFederationChannels(
        this IServiceCollection services, Action<GoogleOptions>? configure = null)
    {
        services.AddOptions<GoogleOptions>().Configure(o => configure?.Invoke(o));
        services.AddOidcDerivedChannels<GoogleOidcChannel>();   // 基座注册 OidcAuthFlow/JwksManager/Validator + 通道入集合
        return services;
    }
}