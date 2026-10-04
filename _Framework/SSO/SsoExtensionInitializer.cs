using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.SSO;

/// <summary>
/// SSO 扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线（开发方案 §5.1）。
/// <para>注册形态（对齐 AuthCenterExtensionInitializer V4.10.53 三态 + ADR92）：
/// <b>门面（AddConstructibleService）</b>——IToken2Service / ISsoAccessCodeService / ISsoClientService /
/// ISsoProfileService（接口 : IDomainService → 接口可构造守卫工厂 + 实现类 throw-factory，消费方经
/// <c>User.Use&lt;接口&gt;()</c> 解析）；<b>多实现集合（TryAddEnumerableConstructible）</b>——
/// <see cref="ISsoChannel"/>（平台适配扩展追加实现，未注册通道自然跳过）。</para>
/// <para>零 DataService 手动注册（ADR61/D17 铁律）——2 实体 DataService 经 SG1 消费方聚合自动注册；
/// Options（SsoOptions）+ 消费方经 Abstractions 契约消费认证内核（ISsoAccountQueryService/ISsoChannelMapService
/// 由 Authentication 主包注册，SSO 经 <c>User.Use&lt;契约&gt;()</c> 帧内解析）。</para>
/// </summary>
[TKWFExtension("SSO")]
public class SsoExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "SSO";

    /// <summary>扩展描述。</summary>
    public override string Description => "认证中心联邦层（SSO）——应用注册 / 授权码 accesscode / token2（ES256 独立密钥域 + JWKS）/ profile API / ISsoChannel IdP 适配器契约";

    /// <summary>
    /// 注册 SSO 服务。
    /// <para>门面（AddConstructibleService——V4.10.53 ADR90 正确路线）：接口 : IDomainService，
    /// 接口可构造守卫工厂 + 实现类 throw-factory，消费方经 <c>User.Use&lt;接口&gt;()</c> 帧内解析；
    /// 多实现集合（TryAddEnumerableConstructible——V4.10.55 ADR92）：ISsoChannel 平台适配扩展追加。</para>
    /// <para>Options：SsoOptions 标 [Options("TKWF:SSO")]——SG1 消费方自动绑定；此处 AddOptions 兜底默认值
    /// （守卫工厂经 ActivatorUtilities 解析剩余参数需 IOptions 可解析，对齐 AuthCenter 先例）。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值兜底（SG1 [Options] 已在消费方自动绑定 TKWF:SSO 节）
        services.AddOptions<SsoOptions>();

        // 门面（AddConstructibleService）——消费方 User.Use<接口>() 帧内解析
        services.AddConstructibleService<IToken2Service, Token2Service>();
        services.AddConstructibleService<ISsoAccessCodeService, SsoAccessCodeService>();
        services.AddConstructibleService<ISsoClientService, SsoClientService>();
        services.AddConstructibleService<ISsoProfileService, SsoProfileService>();
    }

    /// <summary>
    /// 幂等初始化——签名密钥 fail-fast 预检（生产缺 Issuer/EC 密钥 → InvalidOperationException 拒绝启动；
    /// 开发自动生成临时密钥 + Warning）。
    /// <para>经 <see cref="BeginSystemScopeAsync"/> 进入系统作用域 + <c>scope.System.Use&lt;IToken2Service&gt;()</c>
    /// 解析（对齐 AuthCenter V0.3.1 A' 裁定——门面解析需域作用域；IToken2Service : IDomainService 准入）。</para>
    /// </summary>
    public override async Task InitializeAsync(IServiceProvider sp)
    {
        var host = sp.GetRequiredService<DomainHost<TUserInfo>>();
        await using var sysScope = await host.BeginSystemScopeAsync(sp);

        var token2Service = sysScope.System.Use<IToken2Service>();
        if (token2Service is Token2Service concrete)
            concrete.EnsureKeysLoaded(); // 触发 _keys 懒加载（生产 fail-fast）
    }
}
