using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;

namespace TKWF.Federation.Oidc;

/// <summary>
/// OIDC 通道基座注册扩展（组件 7——对齐 WeChat 先例/N1 模板 §2.4 形态）。
/// <para>两种接线：</para>
/// <para>① <c>AddOidcFederationChannel(name, configure)</c>——**自托管 IdP 直配**（Keycloak/Okta/Auth0）：
/// 向 <c>OidcOptions.Channels</c> 追加配置并注册 <see cref="OidcConfiguredChannel"/>（具体包装通道——
/// Oracle P0-1 修正：**抽象基类 <see cref="OidcChannelBase"/> 不可 direct 注册**，DI 无法实例化运行时必炸）；</para>
/// <para>② <c>AddOidcDerivedChannels&lt;TChannel&gt;()</c>——**派生平台库**（M2 Google/Microsoft）：
/// 注册派生通道类型入 <c>IEnumerable&lt;ISsoChannel&gt;</c> 集合（帧内 CurrentAopUser 供给 ctor IDomainUser）。
/// ⚠️ 派生平台库调用后**须自行注册其平台 Options**（基座不感知派生 Options 类型——Oracle P1-6）。</para>
/// <para>未装配库 = 空集合：引用但不调用本扩展 → <c>IEnumerable&lt;ISsoChannel&gt;</c> 帧内解析空数组，自然跳过。</para>
/// </summary>
public static class OidcFederationServiceCollectionExtensions
{
    /// <summary>自托管 IdP 直配（免平台库）——<c>OidcConfiguredChannel</c> 具体包装通道注册。</summary>
    public static IServiceCollection AddOidcFederationChannel(
        this IServiceCollection services, string name, Action<OidcPlatformConfig>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("OIDC 直配通道 name 必填", nameof(name));

        services.AddOptions<OidcOptions>().Configure(o =>
        {
            var cfg = new OidcPlatformConfig { ChannelId = name };
            configure?.Invoke(cfg);
            o.Channels.Add(cfg);
        });

        // OidcChannelFlow（typed client——OIDC 原语委托引擎后：流内自持 JwksManager 缓存 + 引擎协作，无独立 JwksManager/Validator 注册）
        services.AddHttpClient<OidcChannelFlow>();
        // Oracle P0-1：注册具体包装通道（抽象基类不可 direct 注册）
        services.TryAddEnumerableConstructible<ISsoChannel, OidcConfiguredChannel>();
        return services;
    }

    /// <summary>派生平台库（M2）注册——派生通道入集合（<c>DomainService =&gt; 守卫工厂</c>帧内供给）。
    /// ⚠️ 调用方须自行注册其平台 Options（<c>AddOptions&lt;GoogleOptions&gt;().Configure(...)</c>——Oracle P1-6）。</summary>
    public static IServiceCollection AddOidcDerivedChannels<TChannel>(this IServiceCollection services)
        where TChannel : OidcChannelBase
    {
        services.AddHttpClient<OidcChannelFlow>();
        services.TryAddEnumerableConstructible<ISsoChannel, TChannel>();
        return services;
    }
}