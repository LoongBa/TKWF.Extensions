using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;

namespace TKWF.Ext.Federation;

/// <summary>
/// Federation 扩展初始化器（连接层壳——TrustCenter 剥离后瘦身）——经 [TKWFExtension] 被 SG1 发现，三钩子接线。
/// <para>注册形态（对齐 AuthCenterExtensionInitializer V4.10.53 三态 + ADR92）：
/// <b>门面（AddConstructibleService）</b>——ISsoProfileService（server-to-server profile API，唯一信任内核
/// 迁出后保留的门面）/ ISsoChannelRegistryService（通道注册表写入）/ ISsoChannelFactory（通道工厂）/
/// ISsoLogin（登录编排）；<b>普通 DI</b>——StaticChannelRegistry（通道来源聚合）+ DbChannelRegistry（DB 动态权威层）；
/// <b>多实现集合（TryAddEnumerableConstructible）</b>——<see cref="ISsoChannel"/>（契约在 TrustCenter.Abstractions；
/// <b>集合注册责任归平台网关库扩展方法</b> <c>AddXxxFederationChannels()</c> 内部
/// <c>TryAddEnumerableConstructible&lt;ISsoChannel, X&gt;()</c>（方案 §5.4 Oracle P1-4/评审点 7），
/// Federation 初始器零 ISsoChannel 具体实现故<b>不注册任何集合元素</b>——未装配库时
/// <c>IEnumerable&lt;ISsoChannel&gt;</c> 帧内解析为空（"未注册通道自然跳过" F5）。</para>
/// <para><b>TrustCenter 剥离（2026-10-09）</b>：信任内核（IToken2Service/ISsoAccessCodeService/ISsoClientService/
/// SsoClientEntity/SsoAccessCodeEntity/FederationOptions）迁 TrustCenter——本初始器删除 3 信任门面注册 +
/// DevKeyCache + FederationOptions 绑定；<c>[TKWFExtensionCapability(IToken2Service)]</c> 删除（token2 归 TrustCenter）；
/// <c>[TKWFExtensionDependency(ISsoAccountQueryService)]</c> 保留（SsoProfileService 仍消费 AuthCenter.Abstractions 契约）。</para>
/// </summary>
[TKWFExtension("Federation")]
    [TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.AuthCenter.ISsoAccountQueryService), MinVersion = "0.1.0")]
    public class FederationExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "Federation";

    /// <summary>扩展描述。</summary>
    public override string Description => "联邦互联连接层（Federation——外部统一出口，TrustCenter 剥离后）——多通道配置持久化（SsoChannelRegistryEntity）/ 通道选区设施（IChannelRegistry 系列）/ 通道工厂 / 登录编排 / profile API；经 TrustCenter.Abstractions 契约消费信任内核（ISsoChannel/IToken2Service/IAccessCodeService）";

    /// <summary>
    /// 注册 Federation 连接层服务。
    /// <para>门面（AddConstructibleService——V4.10.53 ADR90 正确路线）：接口 : IDomainService，
    /// 接口可构造守卫工厂 + 实现类 throw-factory，消费方经 <c>User.Use&lt;接口&gt;()</c> 帧内解析；
    /// 多实现集合（TryAddEnumerableConstructible——V4.10.55 ADR92）：ISsoChannel 集合元素由平台网关库
    /// 扩展方法追加（如 <c>AddWeChatFederationChannels()</c>——方案 §5.4 Oracle P1-4/评审点 7）；
    /// 本初始器零元素注册（连接层无 ISsoChannel 具体实现——集合壳归库扩展，未装配自然跳过）。</para>
    /// <para>Options：<see cref="FederationStaticChannelOptions"/>（TKWF:Federation:Channels——跨平台通用设置）
    /// + <see cref="FederationChannelRegistryOptions"/>（TKWF:Federation:ChannelRegistry——通道注册表加密密钥路径，
    /// TrustCenter 剥离后独立承载）；此处 AddOptions 兜底默认值（守卫工厂经 ActivatorUtilities 解析剩余参数需
    /// IOptions 可解析，对齐 AuthCenter 先例）。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值兜底（SG1 [Options] 已在消费方自动绑定 TKWF:Federation:Channels / :ChannelRegistry 节）
        services.AddOptions<FederationStaticChannelOptions>();
        services.AddOptions<FederationChannelRegistryOptions>();

        // 对称密钥提供者（keyed singleton，键 SymmetricKeyProviderKeys.Federation——主框架常量已存在，直接复用）
        // ——通道注册表（SsoChannelRegistryEntity）AppSecret/ExtraJson AES-GCM 加解密密钥：
        // 构造即加载，生产缺密钥 fail-fast / 开发按 FileSymmetricKeyProvider 三态语义。
        // ⚠️ TrustCenter 剥离：信任内核（SsoClientService）已迁 TrustCenter（密钥键切主框架 SymmetricKeyProviderKeys.TrustCenter，
        // v4.10.70 并入），本注册保留供连接层通道注册表使用（FederationOptions 迁出后改读 FederationChannelRegistryOptions）。
        services.AddKeyedSingleton<ISymmetricKeyProvider, FileSymmetricKeyProvider>(
            SymmetricKeyProviderKeys.Federation,
            (sp, _) =>
            {
                var o = sp.GetRequiredService<IOptions<FederationChannelRegistryOptions>>().Value;
                return new FileSymmetricKeyProvider(o.SecretEncryptionKeyPath, o.IsProduction, sp.GetService<ILogger<FileSymmetricKeyProvider>>());
            });

        // ── 多通道联邦（v0.3.0，方案 docs/Federation/多通道联邦-开发方案.md §3.2/3.4）──
        // 通道注册表门面：Composite = 组合语义单点（DB 命中优先 → Static 回退）；
        // StaticChannelRegistry 具体类注册（普通 DI——Composite 守卫工厂 ActivatorUtilities 解析依赖）
        services.AddScoped<StaticChannelRegistry>();
        services.AddConstructibleService<IChannelRegistry, CompositeChannelRegistry>();

        // ── 多通道联邦 Phase 2：DB 动态权威层（P2-2/P2-3）──
        // DbChannelRegistry：DomainServiceBase 派生（ctor 含 IDomainUser + keyed ISymmetricKeyProvider，不可直接 DI 解析）——
        // Composite 经基类 User.Use<DbChannelRegistry>() 帧内 NoAop 直建；DI 注册守卫工厂形态（防直取/ValidateOnBuild 安全）
        services.Add(ServiceDescriptor.Scoped(typeof(DbChannelRegistry), _ => throw new InvalidOperationException(
            "[领域架构守卫] DbChannelRegistry 必须经 User.Use<DbChannelRegistry>() 解析（Composite 帧内 NoAop 直建）")));
        // 通道注册表写入门面（Register/Update/Unregister + AES-GCM 加解密——管理端点不建，服务层方法保留）
        services.AddConstructibleService<ISsoChannelRegistryService, SsoChannelRegistryService>();

        // 通道工厂 + 登录编排门面（Oracle M8：Singleton 守卫工厂，CreateAsync 帧内构造供给 IDomainUser）
        services.AddConstructibleService<ISsoChannelFactory, SsoChannelFactory>();
        services.AddConstructibleService<ISsoLogin, SsoLogin>();

        // 外部 IdP 借道验证桥接（三层边界 2026-10-09，方案 §5.4）——AuthCenter 经 Abstractions 契约
        // 借道 Federation 连接层（IExternalIdpAuthenticator 契约 : IDomainService → 守卫工厂门面形态；
        // 实现继承 DomainServiceBase，消费方 User.Use<IExternalIdpAuthenticator>() 帧内解析——委托平台库通道非双实现）
        services.AddConstructibleService<TKWF.Ext.AuthCenter.IExternalIdpAuthenticator, ExternalIdpAuthenticator>();

        // profile API 门面（server-to-server——scope 强制 + 审计，经 AuthCenter.Abstractions 契约消费认证内核）
        services.AddConstructibleService<ISsoProfileService, SsoProfileService>();
    }
}
