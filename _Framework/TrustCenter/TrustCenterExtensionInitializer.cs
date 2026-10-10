using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKW.Framework.Utility.Caching;

namespace TKWF.Ext.TrustCenter;

/// <summary>
/// TrustCenter 扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线（TrustCenter 剥离开发方案 §5.2）。
/// <para>注册形态（对齐 AuthCenterExtensionInitializer V4.10.53 三态 + ADR92）：
/// <b>门面（AddConstructibleService）</b>——IToken2Service / IAccessCodeService / ISsoClientService
/// （接口 : IDomainService → 接口可构造守卫工厂 + 实现类 throw-factory，消费方经
/// <c>User.Use&lt;接口&gt;()</c> 解析）；<b>多实现集合（TryAddEnumerableConstructible）</b>——
/// <see cref="ISsoChannel"/>（契约在 TrustCenter.Abstractions——Oracle P1-1；<b>集合注册责任归平台网关库扩展方法</b>
/// <c>AddXxxFederationChannels()</c> 内部 <c>TryAddEnumerableConstructible&lt;ISsoChannel, X&gt;()</c>，
/// TrustCenter 初始器零 ISsoChannel 具体实现故<b>不注册任何集合元素</b>——未装配库时
/// <c>IEnumerable&lt;ISsoChannel&gt;</c> 帧内解析为空（"未注册通道自然跳过" F5）；
/// <b>编排面不迁入</b>（ISsoChannelFactory/SsoLogin 归 Federation，方案 §5.2/§5.3）。</para>
/// <para>零 DataService 手动注册（ADR61/D17 铁律）——2 实体 DataService 经 SG1 消费方聚合自动注册；
/// Options（TrustCenterOptions）+ 密钥基础设施（keyed ISymmetricKeyProvider——
/// <see cref="SymmetricKeyProviderKeys.TrustCenter"/>，v4.10.70 主框架常量并入）。</para>
/// </summary>
[TKWFExtension("TrustCenter")]
    [TKWFExtensionCapability(ServiceType = typeof(IToken2Service), QuerySurface = "FullIQueryable")]
    public class TrustCenterExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "TrustCenter";

    /// <summary>扩展描述。</summary>
    public override string Description => "信任中心（TrustCenter，纯信任内核）——应用注册（SsoClientEntity）/ token2（ES256 独立密钥域 + JWKS）/ accesscode / ISsoChannel 契约";

    /// <summary>
    /// 注册 TrustCenter 服务。
    /// <para>门面（AddConstructibleService——V4.10.53 ADR90 正确路线）：接口 : IDomainService，
    /// 接口可构造守卫工厂 + 实现类 throw-factory，消费方经 <c>User.Use&lt;接口&gt;()</c> 帧内解析；
    /// ISsoChannel 集合元素由平台网关库扩展方法追加（如 <c>AddWeChatFederationChannels()</c>）——
    /// 本初始器零元素注册（TrustCenter 无 ISsoChannel 具体实现——集合壳归库扩展，未装配自然跳过）。</para>
    /// <para>Options：TrustCenterOptions 标 [Options("TKWF:TrustCenter")]——SG1 消费方自动绑定；此处 AddOptions 兜底默认值
    /// （守卫工厂经 ActivatorUtilities 解析剩余参数需 IOptions 可解析，对齐 AuthCenter 先例）。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值兜底（SG1 [Options] 已在消费方自动绑定 TKWF:TrustCenter 节）
        services.AddOptions<TrustCenterOptions>();

        // 对称密钥提供者（keyed singleton，键 SymmetricKeyProviderKeys.TrustCenter——v4.10.70 主框架常量）——构造即加载：
        // 生产缺密钥 fail-fast / 开发按 FileSymmetricKeyProvider 三态语义（随机不落盘 / 写盘 / 取前 32 字节）
        services.AddKeyedSingleton<ISymmetricKeyProvider, FileSymmetricKeyProvider>(
            SymmetricKeyProviderKeys.TrustCenter,
            (sp, _) =>
            {
                var o = sp.GetRequiredService<IOptions<TrustCenterOptions>>().Value;
                return new FileSymmetricKeyProvider(o.SecretEncryptionKeyPath, o.IsProduction, sp.GetService<ILogger<FileSymmetricKeyProvider>>());
            });

        // 开发模式临时 EC 密钥缓存（DI 单例——消灭静态缓存，测试可并行）
        services.AddSingleton(new DevKeyCache<Token2Service.EcKeySet>());

        // 门面（AddConstructibleService）——消费方 User.Use<接口>() 帧内解析
        services.AddConstructibleService<IToken2Service, Token2Service>();
        services.AddConstructibleService<IAccessCodeService, AccessCodeService>();
        services.AddConstructibleService<ISsoClientService, SsoClientService>();
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
        await using var sysScope = await host.BeginSystemScopeAsync(); // 不传 sp——框架内部建子 scope（同 AuthCenter V0.5.4 修复）

        var token2Service = sysScope.System.Use<IToken2Service>();
        if (token2Service is Token2Service concrete)
            concrete.EnsureKeysLoaded(); // 触发 _keys 懒加载（生产 fail-fast）
    }
}
