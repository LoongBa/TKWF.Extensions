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
/// <para>ConfigureServices（2026-11-05 拆分改造——通道面下沉 WeChatChannels 后移除通道注册；V4.10.53 领域自治根治，ADR90 正确路线）：</para>
/// <list type="bullet">
/// <item><b>门面（AddConstructibleService）</b>——8 接口 : IDomainService
///     （IMenuService / IReplyRuleService / IMaterialService /
///     ISubscriptionStatService / ILogQueryService + 网关 2 + <b>系统状态聚合
///     IWeChatOperationsStatusService（DMP-Lite 5.8 状态卡——只读聚合，V0.3.0 增）</b>）→
///     接口可构造守卫工厂 + 实现类 throw-factory；
///     消费方统一经 <c>User.Use&lt;接口&gt;()</c> 解析（帧内 CurrentAopUser 供给 ctor IDomainUser）。
///     <b>IChannelConfigService 已随 V0.9.0 拆分下沉 WeChatChannels（契约在 WeChatChannels.Abstractions，
///     依赖双白名单：WeChatChannels + WeChatOperations——S1/O4）</b>。</item>
/// <item><b>事件回复 handler（TryAddEnumerableConstructible）</b>——<see cref="WeChatOperationsReplyHandler"/>
///     （ADR92 集合版守卫工厂——继承平台 <see cref="TKWF.Federation.WeChat.WechatEventReplyHandlerBase"/>
///     二级分发 + 内部组合 template 兜底，Oracle P0 选项 A；消费方无需再注册
///     <see cref="TKWF.Federation.WeChat.WechatTemplateEventReplyHandler"/>）。</item>
/// <item><b>E4 密钥管理（拆分改造）</b>——keyed <see cref="ISymmetricKeyProvider"/>（键
///     <see cref="WeChatChannelsKeyProviderKeys.WeChatChannels"/>——<b>V0.9.0 起不在此注册</b>：
///     注册归 WeChatChannels Initializer（过渡常量在 WeChatChannels.Abstractions，主框架
///     <c>SymmetricKeyProviderKeys.WeChatChannels</c> 并入后切换）；本扩展 MessageGatewayService /
///     WeChatMessageForwardJob（下游端点令牌加解密）经 [FromKeyedServices] <b>消费同一键</b>——
///     跨扩展同一信任域（O4），依赖 WeChatChannels 已注册。</item>
/// <item><b>网关装配</b>（不变）——DownstreamForwardClient typed client + WeChatCircuitBreaker 单例 +
///     系统用户供给桥 + 异步抄送作业注册。</item>
/// <item><b>Options</b>——WeChatOperationsOptions 标 [Options("TKWF:WeChatOperations")]——SG1 消费方自动绑定；
///     此处 AddOptions 兜底默认值（守卫工厂 ActivatorUtilities 解析剩余参数需 IOptions 可解析）。
///     <b>密钥配置（SecretEncryptionKeyPath/IsProduction）已随拆分迁 WeChatChannelsOptions（TKWF:WeChatChannels 节）</b>。</item>
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
// 2026-11-05 拆分（V0.9.0）：通道门面契约依赖 WeChatChannels.Abstractions（IChannelConfigService——
//   运营门面经 User.Use<IChannelConfigService> 消费通道；依赖方须先白名单 WeChatChannels）。
// ⚠️ MinVersion 0.0.0-preview.0：契约包首发 v0.1.0 tag 前为 preview 版本（DEP002 按引用程序集实际版本
//   SemVer 比较——0.0.0-preview.x < 0.0.0 故不能用正式版下限），发布后提升为 0.1.0
//   （对齐 Federation.Abstractions 首发先例：tag 后设正式版编译门）
[TKWFExtensionDependency(DependencyType = typeof(TKWF.Ext.WeChatChannels.IChannelConfigService), MinVersion = "0.0.0-preview.0")]
public class WeChatOperationsExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "WeChatOperations";

    /// <summary>扩展描述。</summary>
    public override string Description
        => "微信公众号运营——素材库 / 全消息类型回复规则（欢迎语/关键词/Click 映射/消息回复/Scan/Default）/ 菜单两阶段（拉取/发布/快照/版本/回滚）+ 订阅统计 + 交互/发布日志 + 事件回复 handler + 消息转发网关；通道底座经依赖扩展 WeChatChannels（复用 IChannelConfigService，不重复实现）";

    /// <summary>
    /// ConfigureServices（V0.9.0 拆分改造——移除通道面注册；见类注释）。
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值兜底（SG1 [Options] 消费方自动绑定 TKWF:WeChatOperations 节）
        services.AddOptions<WeChatOperationsOptions>();

        // V0.2.0 网关配置节（TKWF:WeChatOperations:Gateway——独立子域 fail-closed 默认关；守卫工厂
        //    ActivatorUtilities 解析剩余参数需 IOptions 可解析——handler ctor 增 gatewayOptions 后此兜底必需）
        services.AddOptions<WeChatMessageGatewayOptions>();

        // 事件回复 handler（ADR92 集合版守卫工厂——平台基类二级分发 + 内部 template 兜底；
        //   V0.9.0 拆分保留——handler 为运营侧能力，不随通道面下沉）
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
        // V0.9.0 拆分：IChannelConfigService 已下沉 WeChatChannels（本行删除——依赖双白名单）
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
        //   自动回复命中率/转发成功率；只读聚合——ChannelConfig AOP 解析（IChannelConfigService）+
        //   DataService Count 原语）
        services.AddConstructibleService<IWeChatOperationsStatusService, WeChatOperationsStatusService>();

        // D03 1.10 运营配置面：管理门面（5 域 GET/PUT + 乐观并发）+ 只读查询（契约在 FA——Federation 编排链
        //   经 User.Use<IOperationsConfigQuery>() 帧内解析降级文案/模板/TicketMode）
        services.AddConstructibleService<IOperationsConfigService, OperationsConfigService>();
        services.AddConstructibleService<IOperationsConfigQuery, WeChatOperationsConfigQuery>();
    }

    // ConfigureFilters: 空（非过滤器扩展）
    // InitializeAsync: 空（无种子/无持久化前置——配置/素材/规则由运营侧经门面写入，两源并存降级 Options 兜底）
}