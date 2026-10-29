using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.BackgroundJobs;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Ext.Federation;
using TKWF.Federation.WeChat.MessageGateway;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信公众号运营扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线（设计文档 §3.3/§5.11）。
/// <para>ConfigureServices（V4.10.53 领域自治根治，ADR90 正确路线 + E4 密钥管理 + 两源并存装配）：</para>
/// <list type="bullet">
/// <item><b>门面（AddConstructibleService）</b>——9 接口 : IDomainService
///     （IChannelConfigService / IMenuService / IReplyRuleService / IMaterialService /
///     ISubscriptionStatService / ILogQueryService + 网关 2 + <b>系统状态聚合
///     IWeChatOperationsStatusService（DMP-Lite 5.8 状态卡——只读聚合，V0.3.0 增）</b>）→
///     接口可构造守卫工厂 + 实现类 throw-factory；
///     消费方统一经 <c>User.Use&lt;接口&gt;()</c> 解析（帧内 CurrentAopUser 供给 ctor IDomainUser）。</item>
/// <item><b>事件回复 handler（TryAddEnumerableConstructible）</b>——<see cref="WeChatOperationsReplyHandler"/>
///     （ADR92 集合版守卫工厂——继承平台 <see cref="TKWF.Federation.WeChat.WechatEventReplyHandlerBase"/>
///     二级分发 + 内部组合 template 兜底，Oracle P0 选项 A；消费方无需再注册
///     <see cref="TKWF.Federation.WeChat.WechatTemplateEventReplyHandler"/>）。</item>
/// <item><b>两源并存（设计 §3.3.4）</b>——① <b>DB 凭证解析器</b>：<see cref="DbWeChatCredentialResolver"/>
///     （契约 <see cref="TKWF.Federation.WeChat.IWeChatCredentialResolver"/> <b>非 IDomainService</b>——
///     普通 DI TryAddEnumerable Scoped；追加在平台库 <c>AddWeChatFederationChannels()</c> 注册的缺省
///     Options 实现（TryAddSingleton）<b>之后</b>——DI 单解析取最后注册 → DB 优先生效）；
///     ② <b>DB 通道来源</b>：<see cref="WeChatChannelRegistrySource"/>（IChannelSource 普通 DI 集合——
///     Priority=100 恒先于静态源 0，组合层按 Priority 降序首命中，<b>不依赖注册顺序</b>；
///     取数经 <c>IUserBoundChannelSource</c> 由 Composite 构造时绑定域用户 + <c>User.Use&lt;DataService&gt;()</c>
///     NoAop 直建——DataService 的 DI 注册为 throw-factory，不可 IServiceProvider 直取）。</item>
/// <item><b>E4 密钥管理</b>——keyed <see cref="ISymmetricKeyProvider"/>（键
///     <see cref="WeChatOperationsKeyProviderKeys.WeChatOperations"/>——C11 第 6 扩展边界自建常量，
///     FileSymmetricKeyProvider 构造即加载：生产缺密钥 fail-fast / 开发随机兜底）——
///     ChannelConfigService（加密落库）/ DbWeChatCredentialResolver（解密出站）ctor 经 [FromKeyedServices] 注入。</item>
/// <item><b>Options</b>——WeChatOperationsOptions 标 [Options("TKWF:WeChatOperations")]——SG1 消费方自动绑定；
///     此处 AddOptions 兜底默认值（守卫工厂 ActivatorUtilities 解析剩余参数需 IOptions 可解析）。</item>
/// </list>
/// </summary>
[TKWFExtension("WeChatOperations")]
[TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.FeatureManagement.IFeatureManager), MinVersion = "0.4.0")]
// 2026-10-18 ④ 裁定：通道编排契约迁 Federation.Abstractions 独立契约包（版本线独立——V0.1.0 起步，
//   MinVersion 对齐契约包版本而非 Federation 主包；namespace 保持 TKWF.Ext.Federation 跨程序集同）
// ✅ I2-10 + I2 发布：MinVersion 0.1.0 → 0.2.0（Federation.Abstractions/v0.2.0 tag 已创建发布——
//   关注域契约收口 + 通道契约破坏性演进；DEP002 门控按引用程序集版本校验，发布后设 0.2.0 编译门通过）
[TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.Federation.IChannelRegistry), MinVersion = "0.2.0")]
[TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.Federation.IEventReplyHandler), MinVersion = "0.2.0")]
[TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.Federation.IChannelSource), MinVersion = "0.2.0")]
public class WeChatOperationsExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "WeChatOperations";

    /// <summary>扩展描述。</summary>
    public override string Description
        => "微信公众号运营——公众号渠道配置（DB 权威 + Options 降级两源并存）/ 素材库 / 全消息类型回复规则（欢迎语/关键词/Click 映射/消息回复/Scan/Default）/ 菜单两阶段（拉取/发布/快照/版本/回滚）+ 订阅统计 + 交互/发布日志 + 事件回复 handler（内部组合 template 兜底）；通讯走平台库 Federation.WeChat";

    /// <summary>
    /// ConfigureServices（三态注册 + 两源并存装配——见类注释）。
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值兜底（SG1 [Options] 消费方自动绑定 TKWF:WeChatOperations 节）
        services.AddOptions<WeChatOperationsOptions>();

        // V0.2.0 网关配置节（TKWF:WeChatOperations:Gateway——独立子域 fail-closed 默认关；守卫工厂
        //    ActivatorUtilities 解析剩余参数需 IOptions 可解析——handler ctor 增 gatewayOptions 后此兜底必需）
        services.AddOptions<WeChatMessageGatewayOptions>();

        // E4 密钥管理（对齐 AuthCenter/AuthSurface 先例）：keyed ISymmetricKeyProvider 单例
        // （FileSymmetricKeyProvider——构造即加载：生产缺密钥 fail-fast / 开发随机兜底；惰性构造首次解析门面触发）
        services.AddKeyedSingleton<ISymmetricKeyProvider, FileSymmetricKeyProvider>(WeChatOperationsKeyProviderKeys.WeChatOperations, (sp, _) =>
        {
            var o = sp.GetRequiredService<IOptions<WeChatOperationsOptions>>().Value;
            return new FileSymmetricKeyProvider(o.SecretEncryptionKeyPath, o.IsProduction, sp.GetService<ILogger<FileSymmetricKeyProvider>>());
        });

        // 两源并存 ①：DB 凭证解析器（契约非 IDomainService——普通 DI 集合追加；DI 单解析取最后注册 = DB 优先，
        //   未命中内部降级 Options——设计 §3.2.2 Oracle P1）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<TKWF.Federation.WeChat.IWeChatCredentialResolver, DbWeChatCredentialResolver>());

        // 两源并存 ②：DB 通道来源（D03 1.7——WeChatChannelRegistrySource 优先权语义替代 Insert(0) 前置：
        //   Priority=100 高优先（DB 权威恒先于静态源 0 / Federation 主包 SsoChannelRegistryEntity 系 0——
        //   组合按 Priority 降序首命中，方案 §8.4 条件 5；含 disabled 全行投影——S3 降级判据）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChannelSource, WeChatChannelRegistrySource>());

        // 事件回复 handler（ADR92 集合版守卫工厂——平台基类二级分发 + 内部 template 兜底）
        services.TryAddEnumerableConstructible<IEventReplyHandler, WeChatOperationsReplyHandler>();

        // V0.2.0 网关装配：
        //   ① 下游转发 typed client（AddHttpClient<DownstreamForwardClient>——DI 生命周期托管防 socket 悬挂，
        //      对齐平台库 WeChatApiClient 先例；WeChatMessageForwardJob/MessageGatewayService 内注入）
        services.AddHttpClient<DownstreamForwardClient>();

        //   ①b V0.3.0 熔断器（运行库 WeChatCircuitBreaker——进程内三态单例，每 EndpointId 独立状态；
        //      进程重启即清零对齐纯内存先例；MessageGatewayService SyncProxy 出站熔断判定注入）
        services.AddSingleton<WeChatCircuitBreaker>();

        //   ② 系统用户供给桥（BackgroundJob 作业 SystemActor 帧内取系统用户——泛型 accessor 闭包注册，
        //      作业非泛型无法直接引用；对齐 BackgroundJobs JobExecutionRecorder<TUserInfo> SystemUser 先例）
        services.TryAddSingleton<IDomainUserAccessor<TUserInfo>, StandaloneDomainUserAccessor<TUserInfo>>();
        services.AddSingleton<IWeChatSystemUserProvider>(sp =>
        {
            var accessor = sp.GetRequiredService<IDomainUserAccessor<TUserInfo>>();
            return new DelegateWeChatSystemUserProvider(() => accessor.DomainUser);
        });

        //   ③ 异步抄送作业 DI 注册（Worker 经 sp.GetRequiredService(jobType) 解析——消费方 AddBackgroundJobs
        //      的 SG 类型清单归消费方编译，扩展作业不被自动登记，须显式 AddTransient；双注册幂等）
        services.AddTransient<WeChatMessageForwardJob>();
        services.AddTransient(typeof(IBackgroundJob), typeof(WeChatMessageForwardJob));

        // 门面（ADR90 正确路线——接口可构造守卫工厂 + 实现类 throw-factory；消费方 User.Use<接口>() 解析）
        services.AddConstructibleService<IChannelConfigService, ChannelConfigService>();
        services.AddConstructibleService<IMenuService, MenuService>();
        services.AddConstructibleService<IReplyRuleService, ReplyRuleService>();
        services.AddConstructibleService<IMaterialService, MaterialService>();
        services.AddConstructibleService<ISubscriptionStatService, SubscriptionStatService>();
        services.AddConstructibleService<ILogQueryService, LogQueryService>();

        // V0.2.0 网关门面（转发编排核心 RouteAsync + 路由规则/下游端点 CRUD + 网关状态）——
        //   异步抄送门面（BackgroundJob 队列，守卫工厂帧内调用）
        services.AddConstructibleService<IMessageGatewayService, MessageGatewayService>();
        services.AddConstructibleService<IMessageForwardDispatcher, MessageForwardDispatcher>();

        // V0.3.0 系统状态聚合门面（DMP-Lite 5.8 "系统状态"页四卡——公众号连接状态/今日消息量/
        //   自动回复命中率/转发成功率；只读聚合——ChannelConfig AOP 解析 + DataService Count 原语）
        services.AddConstructibleService<IWeChatOperationsStatusService, WeChatOperationsStatusService>();
    }

    // ConfigureFilters: 空（非过滤器扩展）
    // InitializeAsync: 空（无种子/无持久化前置——配置/素材/规则由运营侧经门面写入，两源并存降级 Options 兜底）
}