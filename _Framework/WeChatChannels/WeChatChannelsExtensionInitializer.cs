using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Ext.Federation;
using TKWF.Federation.WeChat;

namespace TKWF.Ext.WeChatChannels;

/// <summary>
/// 微信公众号通道底座扩展初始化器（2026-11-05 拆分下沉）——经 [TKWFExtension] 被 SG1 发现，三钩子接线。
/// <para>ConfigureServices（对齐 WeChatOperations V0.4.0+ 既有装配——通道面下沉）：</para>
/// <list type="bullet">
/// <item><b>门面（AddConstructibleService）</b>——<see cref="IChannelConfigService"/>（契约在
///     WeChatChannels.Abstractions，实现 <see cref="ChannelConfigService"/>）→ 接口可构造守卫工厂 +
///     实现类 throw-factory；消费方统一经 <c>User.Use&lt;接口&gt;()</c> 解析（帧内 CurrentAopUser 供给 ctor IDomainUser）。</item>
/// <item><b>E4 密钥管理</b>——keyed <see cref="ISymmetricKeyProvider"/>（键
///     <see cref="WeChatChannelsKeyProviderKeys.WeChatChannels"/>——过渡常量，主框架
///     <c>SymmetricKeyProviderKeys.WeChatChannels</c> 并入后切换；FileSymmetricKeyProvider 构造即加载：
///     生产缺密钥 fail-fast / 开发随机兜底）——ChannelConfigService（加密落库）/ DbWeChatCredentialResolver
///     （解密出站）/ WeChatChannelRegistrySource（投影解密）ctor 经 [FromKeyedServices] 注入；
///     <b>运营扩展网关经同一键消费本 provider（O4——跨扩展同一信任域，本扩展须先注册）</b>。</item>
/// <item><b>两源并存</b>——① <b>DB 凭证解析器</b>：<see cref="DbWeChatCredentialResolver"/>
///     （契约 <see cref="IWeChatCredentialResolver"/> <b>非 IDomainService</b>——普通 DI TryAddEnumerable Scoped；
///     追加在平台库 <c>AddWeChatFederationChannels()</c> 注册的缺省 Options 实现（TryAddSingleton）<b>之后</b>——
///     DI 单解析取最后注册 → DB 优先生效）；
///     ② <b>DB 通道来源</b>：<see cref="WeChatChannelRegistrySource"/>（<see cref="IChannelSource"/> 普通 DI 集合——
///     Priority=100 恒先于静态源 0，组合层按 Priority 降序首命中，<b>不依赖注册顺序</b>；
///     取数经 <c>IUserBoundChannelSource</c> 由 Composite 构造时绑定域用户 + <c>User.Use&lt;DataService&gt;()</c>
///     NoAop 直建——DataService 的 DI 注册为 throw-factory，不可 IServiceProvider 直取）。</item>
/// <item><b>Options</b>——WeChatChannelsOptions 标 [Options("TKWF:WeChatChannels")]——SG1 消费方自动绑定；
///     此处 AddOptions 兜底默认值（守卫工厂 ActivatorUtilities 解析剩余参数需 IOptions 可解析）。</item>
/// </list>
/// </summary>
[TKWFExtension("WeChatChannels")]
[TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.Federation.IChannelRegistry), MinVersion = "0.2.0")]
[TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.Federation.IChannelSource), MinVersion = "0.2.0")]
[TKWFExtensionDependency(DependencyType = typeof(TKWF.Federation.WeChat.IWeChatCredentialResolver), MinVersion = "0.17.0")]
public class WeChatChannelsExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "WeChatChannels";

    /// <summary>扩展描述。</summary>
    public override string Description
        => "微信公众号通道底座——多通道接入管理（通道配置 CRUD 凭证 AES-GCM 密文落库/DB 凭证解析两源并存/通道注册源 IChannelSource Priority=100/通道能力派生）；平台模式装配，零运营依赖";

    /// <summary>
    /// ConfigureServices（门面 + 密钥 + 两源并存——见类注释）。
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值兜底（SG1 [Options] 消费方自动绑定 TKWF:WeChatChannels 节）
        services.AddOptions<WeChatChannelsOptions>();

        // E4 密钥管理（对齐 AuthCenter/AuthSurface/WeChatOperations 先例）：keyed ISymmetricKeyProvider 单例
        // （FileSymmetricKeyProvider——构造即加载：生产缺密钥 fail-fast / 开发随机兜底；惰性构造首次解析门面触发）
        services.AddKeyedSingleton<ISymmetricKeyProvider, FileSymmetricKeyProvider>(WeChatChannelsKeyProviderKeys.WeChatChannels, (sp, _) =>
        {
            var o = sp.GetRequiredService<IOptions<WeChatChannelsOptions>>().Value;
            return new FileSymmetricKeyProvider(o.SecretEncryptionKeyPath, o.IsProduction, sp.GetService<ILogger<FileSymmetricKeyProvider>>());
        });

        // 两源并存 ①：DB 凭证解析器（契约非 IDomainService——普通 DI 集合追加；DI 单解析取最后注册 = DB 优先，
        //   未命中内部降级 Options——设计 §3.2.2 Oracle P1）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeChatCredentialResolver, DbWeChatCredentialResolver>());

        // 两源并存 ②：DB 通道来源（D03 1.7——WeChatChannelRegistrySource 优先权语义替代 Insert(0) 前置：
        //   Priority=100 高优先（DB 权威恒先于静态源 0 / Federation 主包 SsoChannelRegistryEntity 系 0——
        //   组合按 Priority 降序首命中，方案 §8.4 条件 5；含 disabled 全行投影——S3 降级判据）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChannelSource, WeChatChannelRegistrySource>());

        // 门面（ADR90 正确路线——接口可构造守卫工厂 + 实现类 throw-factory；消费方 User.Use<接口>() 解析）
        services.AddConstructibleService<IChannelConfigService, ChannelConfigService>();
    }

    // ConfigureFilters: 空（非过滤器扩展）
    // InitializeAsync: 空（通道配置由平台/运营侧经门面写入，两源并存降级 Options 兜底）
}