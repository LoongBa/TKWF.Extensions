using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using TKWF.Federation.WeChat;
using TKWF.Federation.WeChat.MessageGateway;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// 微信公众号运营扩展测试宿主——生产路径（真实 DI：扩展 <see cref="WeChatOperationsExtensionInitializer{TUserInfo}"/>
/// ConfigureServices 三态注册 + 平台库 <c>AddWeChatFederationChannels</c> + Federation 多通道核心门面最小等价面 +
/// FreeSql SQLite <b>文件模式</b> + 真实 BindScope + Stub HttpClientFactory 微信 API 桩）。
/// <para>⚠️ 数据库形态裁定：<b>文件模式</b>（对齐 FileManagement 并发测试先例）——SQLite <c>:memory:</c> 在 FreeSql
/// 连接池下每连接独立内存库（多 UoW/DAC 或池轮转取到不同连接 = 不同库），同键 upsert（SubscriptionStat 唯一索引）
/// 会间歇撞 UNIQUE / 快照断言读到空库；文件模式多连接共享同一库文件由文件锁协调，语义可靠。</para>
/// <para>对齐实体型扩展（Settings）与平台库（Federation.WeChat）测试宿主形态：测试类继承本类，各测试内
/// 先设置 <see cref="Handler"/>/<see cref="ConfigureWeChatOptions"/>/<see cref="ConfigureOperationsOptions"/>
/// 再调用 <c>BindScope()</c>，经 <c>User.Use&lt;接口&gt;()</c> AOP 解析门面；<c>using var _ = this</c> 触发
/// UnBindScope + 清理临时库文件。</para>
/// </summary>
public class WeChatOperationsTestHost : IDisposable
{
    /// <summary>FeatureManagement 测试面 MetaContext 换装锁（进程级静态 ProjectMetaContextBase.Instance——镜像
    /// FeatureManagementTestHost s_metaContextLock 串行化语义，防并行用例交叉污染）。</summary>
    private static readonly object s_featureMetaContextLock = new();

    private ServiceProvider? _provider;
    private string? _dbPath;
    private bool _disposed;

    /// <summary>微信 API 桩 handler（默认正向：access_token 可用 + 菜单 JSON 可配置）。</summary>
    public HttpMessageHandler Handler { get; set; } = WeChatOperationsTestSupport.CreateStubHandler();

    /// <summary>WeChatOptions 编程覆盖（Channels 配置 + Extra 模板——平台库配置节）。</summary>
    public Action<WeChatOptions>? ConfigureWeChatOptions { get; set; }

    /// <summary>WeChatOperationsOptions 编程覆盖（DefaultReplyText 等——扩展领域配置节）。</summary>
    public Action<WeChatOperationsOptions>? ConfigureOperationsOptions { get; set; }

    /// <summary>WeChatMessageGatewayOptions 编程覆盖（GatewayEnabled 等——网关独立配置节；V0.2.0 增网关模块）。</summary>
    public Action<WeChatMessageGatewayOptions>? ConfigureGatewayOptions { get; set; }

    /// <summary>熔断器测试覆盖（V0.3.0 SyncProxy——非 null 时每次 BindScope 注册<b>新实例</b>覆盖初始器进程共享单例：
    /// 测试隔离（防跨测试状态泄漏）+ 可配置阈值/开窗期（熔断三态测试控制）。null = 用初始器注册的共享单例。</summary>
    public WeChatCircuitBreaker? CircuitBreaker { get; set; }

    /// <summary>额外通道来源（测试专属 IChannelSource 集合元素——承载 <c>Channel.Extra["news_reply"]</c> 模板等
    /// Options 投影无法承载的键；IChannelSource 为开放集合（TryAddEnumerable），生产允许注册多来源，
    /// StaticChannelRegistry 聚合选区——测试经此注入 template 通道，最小生产等价面扩展）。</summary>
    public IReadOnlyList<TKWF.Ext.Federation.ChannelConfig>? ExtraChannels { get; set; }

    /// <summary>绑定域作用域（生产路径等价）：真实 DI + FreeSql 文件库 + 真实 <see cref="DomainUser{TUserInfo}"/>
    /// BindScope。返回 <c>(provider, user)</c>——经 <c>User.Use&lt;接口&gt;()</c> AOP 路径解析门面
    /// （AddConstructibleService 守卫工厂帧内供给）。</summary>
    public (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var fsql = CreateFileFreeSql(out _dbPath);
        SyncStructure(fsql);
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        ConfigureHost(services);
        var provider = services.BuildServiceProvider();
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };
        _provider = provider;
        return (provider, user);
    }

    /// <summary>SQLite 文件模式库（每次新建唯一临时文件——对齐 FileManagement CreateFileFreeSql 先例）。
    /// <para>⚠️ <c>DateTimeKind=Utc</c>：System.Data.SQLite 默认对 Kind=Utc 的 DateTime 转本地墙钟存储
    /// （UtcNow.Date 00:00 存成 08:00，读回 Unspecified）——生产代码 <c>s.StatDate == statDate</c>（upsert 键）
    /// 在 SQLite 下恒不匹配（撞唯一约束）。DateTimeKind=Utc 统一 UTC 存储语义消除该错位
    /// （实现层契约——对齐生产部署 PG 的 timestamp with time zone 语义）。</para></summary>
    private static IFreeSql CreateFileFreeSql(out string dbPath)
    {
        dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tkwf-wxops-db-" + Guid.NewGuid().ToString("N") + ".db");
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={dbPath};DateTimeKind=Utc")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>实体表结构同步（全部 7 运营 + 4 新网关实体——扩展数据底座；I2-10 删 SubscriptionStatEntity
    /// （12→11），补关注域两实体（Federation 主包——订阅统计委托数据源）。</summary>
    protected void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<ReplyRuleEntity>();
        fsql.CodeFirst.SyncStructure<MaterialEntity>();
        fsql.CodeFirst.SyncStructure<WeChatChannelConfigEntity>();
        fsql.CodeFirst.SyncStructure<MenuSnapshotEntity>();
        fsql.CodeFirst.SyncStructure<MenuVersionEntity>();
        fsql.CodeFirst.SyncStructure<MenuPublishLogEntity>();
        fsql.CodeFirst.SyncStructure<InteractionLogEntity>();
        // V0.2.0 网关模块 4 实体（路由规则/下游端点/转发日志/幂等）
        fsql.CodeFirst.SyncStructure<MessageRouteRuleEntity>();
        fsql.CodeFirst.SyncStructure<DownstreamEndpointEntity>();
        fsql.CodeFirst.SyncStructure<MessageForwardLogEntity>();
        fsql.CodeFirst.SyncStructure<WeChatMessageIdempotencyEntity>();
        // V0.4.0 ③ FeatureManagement 消费面——FeatureValue 表（消息业务总开关 DB 存值；IsEnabledAsync 查库路径）
        fsql.CodeFirst.SyncStructure<TKWF.Ext.FeatureManagement.FeatureValueEntity>();
        // I2-10 关注域（Federation 主包——订阅统计委托 IFollowStateService 数据源：FollowEvent/FollowState 两表）
        fsql.CodeFirst.SyncStructure<TKWF.Ext.Federation.FollowEventEntity>();
        fsql.CodeFirst.SyncStructure<TKWF.Ext.Federation.FollowStateEntity>();
    }

    /// <summary>额外服务/基础设施注册（AddLogging + IFreeSql + UnitOfWorkManager 已在 BindScope 注册——见上）。</summary>
    protected void ConfigureHost(IServiceCollection services)
    {
        // ① 扩展装配——生产形态（消费方白名单启用后三钩子执行 ConfigureServices：
        //    6 门面 AddConstructibleService + handler TryAddEnumerableConstructible + keyed ISymmetricKeyProvider +
        //    DbWeChatCredentialResolver（TryAddEnumerable Scoped）+ DbWeChatChannelSource（Insert(0) 前置）+ AddOptions）
        new WeChatOperationsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // ①b V0.4.0 ③ FeatureManagement 消费面（消息业务总开关 DB 化——生产路径最小等价面，对齐
        //    Extension.FeatureManagement.Tests FeatureManagementTestHost 装配形态）：IMemoryCache + 本地事件总线 +
        //    Feature 贡献者集合（WeChatOperations 自身 Contributor 手动实例化——轻量宿主无消费方 SG1 扫描,
        //    镜像 FeatureTestMetaContext Install 语义）+ FeatureManagement Initializer ConfigureServices。
        services.TryAddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        services.AddSingleton<TKW.Framework.Domain.Events.ILocalEventBus, TKW.Framework.Domain.Events.LocalEventBus>();
        services.AddSingleton<TKW.Framework.Domain.Events.IDistributedEventBus, TKW.Framework.Domain.Events.LocalDistributedEventBus>();
        // FeatureOptions.BindConfiguration 依赖 IConfiguration（空配置——默认值生效，对齐 FeatureManagementTestHost）
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        lock (s_featureMetaContextLock)
        {
            var original = TKW.Framework.CodeGeneration.ProjectMetaContextBase.Instance;
            try
            {
                WeChatOperationsFeatureMetaContext.Install();
                new TKWF.Ext.FeatureManagement.FeatureManagementExtensionInitializer<TestUserInfo>().ConfigureServices(services);
            }
            finally
            {
                WeChatOperationsFeatureMetaContext.Restore(original);
            }
        }

        // ② 平台库装配——AddWeChatFederationChannels（WeChatOptions 编程覆盖 + WeChatApiClient typed client +
        //    OptionsWeChatCredentialResolver 缺省实现 + 双通道 + WeChatChannelSource Options 源）
        services.AddWeChatFederationChannels(o => ConfigureWeChatOptions?.Invoke(o));

        // ③ Federation 多通道核心门面最小等价面（对齐 Extension.Federation.WeChat.Tests 宿主——平台库测试宿主
        //    不启动 FederationExtensionInitializer，补注册"已启用 Federation"的最小生产等价面）：
        //    StaticChannelRegistry + CompositeChannelRegistry + SsoChannelFactory + SsoLogin
        //    （handler 的 ResolveChannelAsync 经 User.Use<IChannelRegistry>() 帧内解析——Composite 组合语义）
        services.AddOptions<FederationStaticChannelOptions>();
        services.AddScoped<StaticChannelRegistry>();
        services.AddConstructibleService<IChannelRegistry, CompositeChannelRegistry>();
        services.AddConstructibleService<ISsoChannelFactory, SsoChannelFactory>();
        services.AddConstructibleService<ISsoLogin, SsoLogin>();
        // 免登入口门面（I2-8——本宿主聚焦运营回复/统计，免登签票断言归 Federation.Tests：
        // Fake 返回 null = LoginEntry.Enabled=false 等价——模板 {ticket} → 空串替换，其余文本照常渲染）
        services.AddScoped<ILoginEntryService>(_ => new NullLoginEntryService());
        // I2-10 关注域门面（真实生产路径——订阅统计委托 IFollowStateService.GetDailyStatsAsync；
        //     FollowStateService 为 Federation 主包 internal——InternalsVisibleTo 授权本测试程序集）
        services.AddConstructibleService<IFollowStateService, TKWF.Ext.Federation.FollowStateService>();

        // ④ Stub IHttpClientFactory——微信 API 桩（AddHttpClient 的 DefaultHttpClientFactory 被覆盖）
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(Handler));

        // ⑤ 测试桥接：IDomainUser 注册（DataService ctor 需 IDomainUser——接线型 DbWeChatCredentialResolver/
        //    DbWeChatChannelSource 的 GetRequiredService<DataService>() 需 DI 可构造；生产永不注册 IDomainUser，D01）
        var user = new TestDomainUser();
        services.AddSingleton<IDomainUser>(sp => { user.Provider = sp; return user; });

        // ⑥ IEntityDAC<T> 基础设施（全实体共享 TestHostBase 注册的 Singleton UnitOfWorkManager——
        //    ⚠️ 对齐 Settings/AuthCenter 先例：SQLite :memory: 下 DbContextScopedFreeSql 每 UoW 实例独占连接
        //    （独立内存库）——每 DAC new UoW 会致 8 个独立库写读跨库错乱（upsert 查不到首次创建、撞唯一约束、
        //    快照断言间歇失败的根因）；共享单 UoW = 单连接单库，裸 fsql.Select 断言同库可读）
        services.AddSingleton<IEntityDAC<ReplyRuleEntity>>(sp => new FreeSqlEntityDAC<ReplyRuleEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddSingleton<IEntityDAC<MaterialEntity>>(sp => new FreeSqlEntityDAC<MaterialEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddSingleton<IEntityDAC<WeChatChannelConfigEntity>>(sp => new FreeSqlEntityDAC<WeChatChannelConfigEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddSingleton<IEntityDAC<MenuSnapshotEntity>>(sp => new FreeSqlEntityDAC<MenuSnapshotEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddSingleton<IEntityDAC<MenuVersionEntity>>(sp => new FreeSqlEntityDAC<MenuVersionEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddSingleton<IEntityDAC<MenuPublishLogEntity>>(sp => new FreeSqlEntityDAC<MenuPublishLogEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddSingleton<IEntityDAC<InteractionLogEntity>>(sp => new FreeSqlEntityDAC<InteractionLogEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        // V0.2.0 网关模块 4 实体 IEntityDAC（同 UoW 单连接）
        services.AddSingleton<IEntityDAC<MessageRouteRuleEntity>>(sp => new FreeSqlEntityDAC<MessageRouteRuleEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddSingleton<IEntityDAC<DownstreamEndpointEntity>>(sp => new FreeSqlEntityDAC<DownstreamEndpointEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddSingleton<IEntityDAC<MessageForwardLogEntity>>(sp => new FreeSqlEntityDAC<MessageForwardLogEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddSingleton<IEntityDAC<WeChatMessageIdempotencyEntity>>(sp => new FreeSqlEntityDAC<WeChatMessageIdempotencyEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        // V0.4.0 ③ FeatureManagement 消费面——FeatureValue 实体 IEntityDAC（消息业务总开关 DB 存值路径）
        services.AddSingleton<IEntityDAC<TKWF.Ext.FeatureManagement.FeatureValueEntity>>(sp => new FreeSqlEntityDAC<TKWF.Ext.FeatureManagement.FeatureValueEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        // I2-10 关注域两实体 IEntityDAC（同 UoW 单连接——订阅统计委托数据源）
        services.AddSingleton<IEntityDAC<TKWF.Ext.Federation.FollowEventEntity>>(sp => new FreeSqlEntityDAC<TKWF.Ext.Federation.FollowEventEntity>(sp.GetRequiredService<UnitOfWorkManager>()));
        services.AddSingleton<IEntityDAC<TKWF.Ext.Federation.FollowStateEntity>>(sp => new FreeSqlEntityDAC<TKWF.Ext.Federation.FollowStateEntity>(sp.GetRequiredService<UnitOfWorkManager>()));

        // ⑦ DataService 可构造注册（测试镜像生产 ADR61 可构造工厂，非 throw-factory——接线型 GetRequiredService 需要；
        //    单例对齐 IEntityDAC（⑥）——同 UoW 同连接数据可见）
        services.AddSingleton<ReplyRuleEntityDataService>();
        services.AddSingleton<MaterialEntityDataService>();
        services.AddSingleton<WeChatChannelConfigEntityDataService>();
        services.AddSingleton<MenuSnapshotEntityDataService>();
        services.AddSingleton<MenuVersionEntityDataService>();
        services.AddSingleton<MenuPublishLogEntityDataService>();
        services.AddSingleton<InteractionLogEntityDataService>();
        // V0.2.0 网关模块 4 DataService（镜像生产 ADR61 可构造工厂）
        services.AddSingleton<MessageRouteRuleEntityDataService>();
        services.AddSingleton<DownstreamEndpointEntityDataService>();
        services.AddSingleton<MessageForwardLogEntityDataService>();
        services.AddSingleton<WeChatMessageIdempotencyEntityDataService>();
        // V0.4.0 ③ FeatureManagement 消费面——FeatureValue DataService（消息业务总开关 DB 存值路径）
        services.AddSingleton<TKWF.Ext.FeatureManagement.FeatureValueEntityDataService>();
        // I2-10 关注域两 DataService（Federation 主包——FollowStateService 组合生产路径）
        services.AddSingleton<TKWF.Ext.Federation.FollowEventEntityDataService>();
        services.AddSingleton<TKWF.Ext.Federation.FollowStateEntityDataService>();

        // ⑧ ITransactionManager（MenuService 写路径事务包裹——Noop Begin/Commit 空操作，
        //    DataService 逐操作经 UnitOfWorkManager 持久化）
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();

        // ⑨ 额外通道来源（template 承载等——IChannelSource 开放集合，StaticChannelRegistry 聚合；
        //    ⚠️ 直接 Add（非 TryAddEnumerable）——工厂委托形态 ImplementationType 默认=ServiceType，
        //    TryAddEnumerable 遇同 ServiceType 已有注册抛"indistinguishable"（源码断言实证））
        if (ExtraChannels is { Count: > 0 })
            services.Add(ServiceDescriptor.Scoped<IChannelSource>(_ => new DelegateChannelSource(ExtraChannels)));

        // ⑩ Options 兜底默认值（守卫工厂 ActivatorUtilities 解析剩余参数需 IOptions 可解析——Initializer 已 AddOptions；
        //    此处编程覆盖测试专属值；⚠️ IsProduction 强制 false——测试环境开发模式（FileSymmetricKeyProvider
        //    随机兜底密钥，生产模式缺密钥文件 fail-fast 会拒绝启动））
        services.AddOptions<WeChatOperationsOptions>().Configure(o =>
        {
            ConfigureOperationsOptions?.Invoke(o);
            o.IsProduction = false;
        });

        // ⑩b V0.2.0 网关配置节（独立 Options——GatewayEnabled 默认 false fail-closed；测试编程覆盖入口）
        services.AddOptions<WeChatMessageGatewayOptions>().Configure(o => ConfigureGatewayOptions?.Invoke(o));

        // ⑩c V0.3.0 熔断器覆盖（测试专属——初始器已注册进程共享单例，此处若设 CircuitBreaker 则最后注册
        //    新实例覆盖（DI 单解析取最后注册）——每次 BindScope 新实例 = 测试隔离 + 可配置阈值/开窗期）
        if (CircuitBreaker is not null)
            services.AddSingleton(CircuitBreaker);
    }

    /// <summary>模板方法：UnBindScope + Provider 释放 + 清理临时库文件（测试类 <c>using var _ = this</c> 触发）。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            DomainUser<TestUserInfo>.UnBindScope();
        }
        catch
        {
            // BindScope 未建立的宿主 Dispose 不抛——忽略 UnBindScope 失败
        }
        // 网关运行时开关静态覆盖复位（测试隔离——SetGatewayEnabledAsync 残留防泄漏至并行/后续测试）
        ResetGatewayEnabledOverride();
        _provider?.Dispose();
        if (_dbPath is not null && File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
                // 库文件被占用（罕见）——忽略，系统 temp 清理兜底
            }
        }
    }

    /// <summary>
    /// 网关运行时开关静态覆盖复位（<see cref="MessageGatewayService"/> 的 <c>_gatewayEnabledOverride</c> 为
    /// 进程内静态——<see cref="IMessageGatewayService.SetGatewayEnabledAsync"/> 测试会污染进程级状态，测试隔离
    /// 经反射复位 null（= 未覆盖，回落 Options 配置值）。网关测试类在 BindScope 后调用 + 宿主 Dispose 兜底。
    /// </summary>
    internal static void ResetGatewayEnabledOverride()
    {
        var field = typeof(MessageGatewayService).GetField("_gatewayEnabledOverride", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        field?.SetValue(null, null);
    }

    /// <summary>Stub IHttpClientFactory（返回固定 handler 的 HttpClient——不真打微信）。</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler);
    }

    /// <summary>委托通道来源（测试专属 IChannelSource——固定返回注入的 ChannelConfig 列表）。</summary>
    private sealed class DelegateChannelSource : IChannelSource
    {
        private readonly IReadOnlyList<TKWF.Ext.Federation.ChannelConfig> _channels;

        public DelegateChannelSource(IReadOnlyList<TKWF.Ext.Federation.ChannelConfig> channels)
            => _channels = channels ?? throw new ArgumentNullException(nameof(channels));

        public string PlatformType => "wechat";

        public IReadOnlyList<TKWF.Ext.Federation.ChannelConfig> GetChannels() => _channels;
    }
}

/// <summary>
/// 测试用 ProjectMetaContext（V0.4.0 ③ FeatureManagement 消费面）——override <c>Contributors["Feature"]</c>
/// 返回 <see cref="WeChatOperationsFeatureDefinitionContributor"/> 清单（<c>WeChatOperations.MessageBusiness</c>
/// Boolean DefaultValue=true）。镜像 FeatureManagementTestHost FeatureTestMetaContext 形态——轻量宿主无消费方
/// SG1 扫描，FeatureManagement Initializer <c>CreateContributorInstances</c> 经此读取编译期清单。
/// <c>ProjectMetaContextBase.Instance</c> setter 是 protected，经子类公开 Install/Restore；静态隔离约束（镜像
/// Permissions/Navigation 测试）：进程级单例，宿主锁串行化（<see cref="WeChatOperationsTestHost"/>）。</summary>
internal sealed class WeChatOperationsFeatureMetaContext : TKW.Framework.CodeGeneration.ProjectMetaContextBase
{
    public static void Install() => Instance = new WeChatOperationsFeatureMetaContext();

    public static void Restore(TKW.Framework.CodeGeneration.IProjectMetaContext? original) => Instance = original;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, IReadOnlyList<TKW.Framework.CodeGeneration.ContributorDescriptor>> Contributors => new Dictionary<string, IReadOnlyList<TKW.Framework.CodeGeneration.ContributorDescriptor>>
    {
        [TKW.Framework.CodeGeneration.ContributorTargetKinds.Feature] = new[]
        {
            new TKW.Framework.CodeGeneration.ContributorDescriptor(
                "TKWF.Ext.WeChatOperations.WeChatOperationsFeatureDefinitionContributor",
                "WeChatOperationsFeatureDefinitionContributor",
                typeof(WeChatOperationsFeatureDefinitionContributor),
                TKW.Framework.CodeGeneration.ContributorTargetKinds.Feature),
        },
    };

    /// <summary>编译期实例化 override——测试桩镜像 Initializer 消费路径（CreateContributorInstances，V4.10.32 A+ 阶段 4）。</summary>
    public override IReadOnlyList<object> CreateContributorInstances(string targetKind)
        => targetKind == TKW.Framework.CodeGeneration.ContributorTargetKinds.Feature
            ? new object[] { new WeChatOperationsFeatureDefinitionContributor() }
            : Array.Empty<object>();

    /// <inheritdoc />
    public override TKW.Framework.CodeGeneration.ProjectConfiguration Configuration => null!;

    /// <inheritdoc />
    public override TKW.Framework.CodeGeneration.MetadataChangeLog ChangeLog => null!;

    /// <inheritdoc />
    public override string MetadataSchemaVersion => "test";
}

/// <summary>
/// 微信 API 出站桩公共设施（对齐 Extension.Federation.WeChat.Tests WeChatTestHost 模式）。
/// </summary>
internal static class WeChatOperationsTestSupport
{
    /// <summary>
    /// 微信 API 出站桩 HttpMessageHandler——按 URL 路径路由返回固定 JSON（不真打微信）。
    /// <para>路由 <c>cgi-bin/token</c>（access_token）、<c>cgi-bin/menu/create</c>（记录请求 body 供断言——
    /// 发布全链验证回显）、<c>cgi-bin/get_current_selfmenu_info</c>（菜单查询——<paramref name="menuJson"/>
    /// 可配置）、<c>cgi-bin/menu/delete</c>（菜单删除）。其余路径 404。</para>
    /// </summary>
    public static HttpMessageHandler CreateStubHandler(
        string? menuJson = null,
        List<string>? menuCreateBodies = null)
        => new StubWeChatHandler(menuJson, menuCreateBodies);

    /// <summary>构造语义事件派发（I2 语义层——handler 用例：微信参数包 → 语义 <see cref="ChannelEvent"/>；
    /// RawPayload 由 <c>__decrypted_xml</c> 键承载——handler <c>WechatEventContext.FromChannelEvent</c> 重建数据源）。</summary>
    public static TKWF.Ext.Federation.ChannelEventDispatch CreateEventContext(params (string Key, string? Value)[] parameters)
    {
        var dict = new Dictionary<string, string?>(parameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));
        dict.TryGetValue("channel_id", out var channelId);
        dict.TryGetValue("from_user_name", out var fromUserName);
        dict.TryGetValue("external_user_id", out var externalUserId);
        dict.TryGetValue("event", out var eventValue);
        dict.TryGetValue("__decrypted_xml", out var rawXml);
        var evt = new TKWF.Ext.Federation.ChannelEvent
        {
            ChannelId = channelId ?? "",
            ChannelType = "wechat_event",
            ExternalUserId = externalUserId ?? fromUserName ?? "",
            EventType = eventValue ?? "unknown",
            RawPayload = rawXml,
            Attributes = dict,
        };
        dict.TryGetValue("uid", out var uid);
        return new TKWF.Ext.Federation.ChannelEventDispatch(evt, uid);
    }

    /// <summary>构造微信内层消息 XML（CDATA 包裹——__decrypted_xml 数据源形态）。</summary>
    public static string WxXml(params (string Name, string Value)[] fields)
    {
        var sb = new StringBuilder("<xml>");
        foreach (var (name, value) in fields)
            sb.Append($"<{name}><![CDATA[{value}]]></{name}>");
        return sb.Append("</xml>").ToString();
    }

    /// <summary>生成合法 43 字符 EncodingAESKey（32 字节 Base64 去 '='）。</summary>
    public static string GenerateEncodingAESKey()
    {
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        return Convert.ToBase64String(key).TrimEnd('=');
    }

    private sealed class StubWeChatHandler : HttpMessageHandler
    {
        private readonly string? _menuJson;
        private readonly List<string>? _menuCreateBodies;

        public StubWeChatHandler(string? menuJson, List<string>? menuCreateBodies)
        {
            _menuJson = menuJson;
            _menuCreateBodies = menuCreateBodies;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            string json;
            if (path.EndsWith("/cgi-bin/token", StringComparison.Ordinal))
            {
                json = "{\"access_token\":\"stub-token\",\"expires_in\":7200}";
            }
            else if (path.EndsWith("/cgi-bin/menu/create", StringComparison.Ordinal))
            {
                // 记录请求 body（发布全链断言——menu/create 收到什么）
                if (request.Content is not null)
                {
                    var body = request.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
                    _menuCreateBodies?.Add(body);
                }
                json = "{\"errcode\":0,\"errmsg\":\"ok\"}";
            }
            else if (path.EndsWith("/cgi-bin/get_current_selfmenu_info", StringComparison.Ordinal))
            {
                json = _menuJson
                    ?? "{\"is_menu_open\":1,\"selfmenu_info\":{\"button\":[{\"type\":\"click\",\"name\":\"测试菜单\",\"key\":\"test_key\"}]}}";
            }
            else if (path.EndsWith("/cgi-bin/menu/delete", StringComparison.Ordinal))
            {
                json = "{\"errcode\":0,\"errmsg\":\"ok\"}";
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}

/// <summary>Noop 事务管理器——BeginAsync/CommitAsync 空操作（对齐 OrganizationUnit/FileManagement/Calendar 测试宿主）。</summary>
internal sealed class NoopTransactionManager : ITransactionManager
{
    public bool IsActive => false;

    public ITransactionScope Begin(System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.Serializable)
        => new NoopTransactionScope();

    public Task<ITransactionScope> BeginAsync(
        System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.Serializable,
        CancellationToken ct = default)
        => Task.FromResult<ITransactionScope>(new NoopTransactionScope());
}

/// <summary>Noop 事务作用域——CommitAsync/RollbackAsync 空操作。</summary>
internal sealed class NoopTransactionScope : ITransactionScope
{
    public bool IsActive => true;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
    public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Commit() { }
    public void Rollback() { }
}

/// <summary>
/// 可配置测试用户桩——实现 IDomainUser 最小契约（匿名/认证切换、系统账号、UserId 注入）。
/// <para>Use&lt;T&gt;() 按生产 NoAop 路径等价（具体类 DataService——ActivatorUtilities.CreateInstance，
/// IDomainUser 参数显式传 this，IEntityDAC 等其余从 DI 解析——对齐 FileManagement/Settings 测试桩）。
/// 本扩展门面经真实 <see cref="DomainUser{TUserInfo}"/> AOP 解析（BindScope），桩主要作 DataService ctor 的
/// DI IDomainUser 解析源（接线型 GetRequiredService 需要）。</para>
/// </summary>
internal sealed class TestDomainUser : IDomainUser
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object?> _cache = new();

    /// <summary>ServiceProvider（宿主工厂注入，构建后可用——Use&lt;T&gt; 解析源）。</summary>
    public IServiceProvider? Provider { get; set; }

    public string SessionKey { get; set; } = "test-session";
    public bool IsAuthenticated { get; set; } = false;
    public bool IsSystemActor { get; set; } = false;
    public IUserInfo? UserInfo { get; set; }
    public long? TenantId { get; set; }
    public bool IsNoAuditActive { get; set; } = false;
    public string? UserId { get; set; }
    public string? UserName { get; set; }

    public bool IsInRole(string role) => UserInfo?.IsInRole(role) ?? false;

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
    {
        var provider = Provider ?? throw new NotSupportedException("Stub: Provider 未注入");

        // 具体类（DataService）：NoAop 等价（ActivatorUtilities 直建，IDomainUser 参数显式传 this——IEntityDAC 从 DI 解析）
        if (_cache.TryGetValue(typeof(TDomainService), out var cached) && cached is TDomainService svc)
            return svc;
        lock (_gate)
        {
            if (_cache.TryGetValue(typeof(TDomainService), out cached) && cached is TDomainService svc2)
                return svc2;
            var resolved = (TDomainService)ActivatorUtilities.CreateInstance(provider, typeof(TDomainService), this);
            _cache[typeof(TDomainService)] = resolved;
            return resolved;
        }
    }

    public TService GetService<TService>() where TService : notnull
        => (Provider ?? throw new NotSupportedException("Stub: Provider 未注入")).GetRequiredService<TService>();

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}

/// <summary>免登入口 Fake（返回 null = 未启用语义——运营测试宿主免登签票断言归 Federation.Tests）。</summary>
internal sealed class NullLoginEntryService : ILoginEntryService
{
    public Task<string?> BuildTicketUrlAsync(string channelId, string channelType, string externalUserId, int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> BuildTestTicketUrlAsync(string channelId, string platformId, string channelType, string externalUserId, int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null)
        => Task.FromResult<string?>(null);

    public Task<string?> BuildSilentAuthorizeUrlAsync(string channelId, string returnUrl, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
