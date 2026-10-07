using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKW.Framework.Utility.Caching;

namespace TKWF.Ext.Federation;

/// <summary>
/// Federation 扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线（归层实施开发方案 §5.1）。
/// <para>注册形态（对齐 AuthCenterExtensionInitializer V4.10.53 三态 + ADR92）：
/// <b>门面（AddConstructibleService）</b>——IToken2Service / ISsoAccessCodeService / ISsoClientService /
/// ISsoProfileService（接口 : IDomainService → 接口可构造守卫工厂 + 实现类 throw-factory，消费方经
/// <c>User.Use&lt;接口&gt;()</c> 解析）；<b>多实现集合（TryAddEnumerableConstructible）</b>——
/// <see cref="ISsoChannel"/>（契约归属本扩展——Oracle P1-1；<b>集合注册责任归平台网关库扩展方法</b>
/// <c>AddWeChatFederationChannels()</c> 内部 <c>TryAddEnumerableConstructible&lt;ISsoChannel, X&gt;()</c>
/// （归层实施方案 §5.4 Oracle P1-4/评审点 7），Federation 扩展零 ISsoChannel 具体实现故本初始器
/// <b>不注册任何集合元素</b>——未装配库时 <c>IEnumerable&lt;ISsoChannel&gt;</c> 帧内解析为空（"未注册通道自然跳过" F5））。</para>
/// <para>零 DataService 手动注册（ADR61/D17 铁律）——2 实体 DataService 经 SG1 消费方聚合自动注册；
/// Options（FederationOptions）+ 消费方经 Abstractions 契约消费认证内核（ISsoAccountQueryService/ISsoChannelMapService
/// 由 Authentication 主包注册，Federation 经 <c>User.Use&lt;契约&gt;()</c> 帧内解析）。</para>
/// </summary>
[TKWFExtension("Federation")]
    [TKWFExtensionCapability(ServiceType = typeof(IToken2Service), QuerySurface = "FullIQueryable")]
    [TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.AuthCenter.ISsoAccountQueryService), MinVersion = "0.1.0")]
    public class FederationExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "Federation";

    /// <summary>扩展描述。</summary>
    public override string Description => "认证中心联邦层（Federation，外交部）——应用注册 / 授权码 accesscode / token2（ES256 独立密钥域 + JWKS）/ profile API / ISsoChannel IdP 适配器契约";

    /// <summary>
    /// 注册 Federation 服务。
    /// <para>门面（AddConstructibleService——V4.10.53 ADR90 正确路线）：接口 : IDomainService，
    /// 接口可构造守卫工厂 + 实现类 throw-factory，消费方经 <c>User.Use&lt;接口&gt;()</c> 帧内解析；
    /// 多实现集合（TryAddEnumerableConstructible——V4.10.55 ADR92）：ISsoChannel 集合元素由平台网关库
    /// 扩展方法追加（如 <c>AddWeChatFederationChannels()</c>——归层实施方案 §5.4 Oracle P1-4/评审点 7）；
    /// 本初始器零元素注册（Federation 无 ISsoChannel 具体实现——集合壳归库扩展，未装配自然跳过）。</para>
    /// <para>Options：FederationOptions 标 [Options("TKWF:Federation")]——SG1 消费方自动绑定；此处 AddOptions 兜底默认值
    /// （守卫工厂经 ActivatorUtilities 解析剩余参数需 IOptions 可解析，对齐 AuthCenter 先例）。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值兜底（SG1 [Options] 已在消费方自动绑定 TKWF:Federation 节）
        services.AddOptions<FederationOptions>();

        // 对称密钥提供者（keyed singleton，键 SymmetricKeyProviderKeys.Federation）——构造即加载：
        // 生产缺密钥 fail-fast / 开发按 FileSymmetricKeyProvider 三态语义（随机不落盘 / 写盘 / 取前 32 字节）
        services.AddKeyedSingleton<ISymmetricKeyProvider, FileSymmetricKeyProvider>(
            SymmetricKeyProviderKeys.Federation,
            (sp, _) =>
            {
                var o = sp.GetRequiredService<IOptions<FederationOptions>>().Value;
                return new FileSymmetricKeyProvider(o.SecretEncryptionKeyPath, o.IsProduction, sp.GetService<ILogger<FileSymmetricKeyProvider>>());
            });

        // 开发模式临时 EC 密钥缓存（DI 单例——消灭静态缓存，测试可并行）
        services.AddSingleton(new DevKeyCache<Token2Service.EcKeySet>());

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
    /// <para><b>V0.1.1 修复（Development 模式启动崩溃，同 AuthCenter V0.5.4 defect 复制链）</b>：
    /// <c>BeginSystemScopeAsync(externalSp)</c> 传参按 M1 契约原样绑定不建子 scope——<c>InitializeAsync</c> 的
    /// <c>sp</c> 是根容器，<c>System.Use&lt;IToken2Service&gt;()</c> 从根解析 <c>AddScoped</c> 守卫工厂 →
    /// <c>ValidateScopes=true</c>（Development）必崩。修复：<b>不传参</b> <c>host.BeginSystemScopeAsync()</c>
    /// ——框架内部 <c>CreateScope()</c> 建子 scope 并自拥生命周期。</para>
    /// </summary>
    public override async Task InitializeAsync(IServiceProvider sp)
    {
        var host = sp.GetRequiredService<DomainHost<TUserInfo>>();
        await using var sysScope = await host.BeginSystemScopeAsync(); // V0.1.1：不传 sp——框架内部建子 scope（同 AuthCenter V0.5.4 修复）

        var token2Service = sysScope.System.Use<IToken2Service>();
        if (token2Service is Token2Service concrete)
            concrete.EnsureKeysLoaded(); // 触发 _keys 懒加载（生产 fail-fast）
    }
}
