using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Events;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// I2 编排改造测试（方案 §10 测试矩阵 I2-3 + I2-6）——<see cref="EventOrchestrator"/> §5.1 时序：
/// Rejected 短路 / NotApplicable 跳过 / <b>记录在后（R3）</b> / 域事件仅状态变化发布 / 重复投递不重复发。
/// <para>生产路径宿主（对齐 FollowStateServiceTests）：真实 DI（FederationExtensionInitializer.ConfigureServices +
/// FreeSql SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope</c> +
/// <c>User.Use&lt;IEventOrchestrator&gt;()</c>（AOP 路径守卫工厂）。</para>
/// <para>事件发布断言：订阅 <see cref="ILocalEventBus"/> 经 handler 捕获（真实总线 AOP 感知——帧内收集提交后派发；
/// 断言「仅 StateChanged==true 收到」而非模拟总线）。</para>
/// </summary>
public sealed class EventOrchestratorI2Tests : TestHostBase
{
    private readonly FakeFollowChangedHandler _handler = new();
    private readonly FakeEventChannel _channel = new();
    private readonly EventCollector _events = new();

    protected override TestUserInfo CreateUser() => new("orchestrator-user", "编排测试用户");

    protected override void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<FollowEventEntity>();
        fsql.CodeFirst.SyncStructure<FollowStateEntity>();
    }

    protected override void ConfigureHost(IServiceCollection services)
    {
        new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        FederationTestHost.RegisterEntityDacs(services);

        // 事件收集器（替代真实 ILocalEventBus——收集已派发事件供断言；对齐 Approval 测试先例）
        services.AddSingleton<ILocalEventBus>(sp => _events);
        // 通道工厂 Fake（Replace 守卫工厂——CreateAsync 直接返回 Fake 通道，绕过 Registry 选区/ActivatorUtilities 构造）
        services.Replace(ServiceDescriptor.Scoped<ISsoChannelFactory>(_ => new FakeFactory(_channel)));
        // 事件回复 handler（消费方注册形态——TryAddEnumerableConstructible 等价 AddScoped 注入集合）
        services.AddScoped<IEventReplyHandler>(_ => _handler);
        // uid 只读查询（GetByExternalIdAsync——编排层 openid→UId）
        services.AddScoped<IExternalIdentityService>(_ => new FakeExternalIdService("uid-100"));
    }

    private static ChannelEvent FollowEvent(string openid, DateTime occurredAt)
        => new()
        {
            ChannelId = "mp-1",
            ChannelType = "wechat_event",
            ExternalUserId = openid,
            EventType = "follow",
            OccurredAt = occurredAt,
            ReceivedAt = occurredAt.AddSeconds(1),
        };

    // ── 1. Rejected 短路（§5.1 时序第 3 步——信任根一票否决：不记录、不回退 handler）──

    /// <summary>验签/解密失败（Rejected）：返回 null + handler 不迭代 + 状态不记录。</summary>
    [Fact]
    public async Task Orchestrate_Rejected_ShortCircuits_NoRecordNoHandler()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        _channel.Outcome = ChannelEventOutcome.Rejected;

        var reply = await user.Use<IEventOrchestrator>().OrchestrateAsync(
            "mp-1", "wechat_event", new ChannelInboundContext(new Dictionary<string, string?>()), CancellationToken.None);

        Assert.Null(reply);
        Assert.False(_handler.Invoked);
        // 状态未记录（无 FollowState 行）
        var state = await user.Use<FollowStateEntityDataService>()
            .EntityGetAsync(s => s.ChannelId == "mp-1", CancellationToken.None);
        Assert.Null(state);
    }

    // ── 2. NotApplicable 跳过（§5.1 时序第 3 步——非事件通道无事件可记）──

    /// <summary>非事件通道（NotApplicable）：无 handler 迭代 + 无记录（dispatch 不存在）。</summary>
    [Fact]
    public async Task Orchestrate_NotApplicable_SkipsAll()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        _channel.Outcome = ChannelEventOutcome.NotApplicable;

        var reply = await user.Use<IEventOrchestrator>().OrchestrateAsync(
            "mp-1", "wechat_event", new ChannelInboundContext(new Dictionary<string, string?>()), CancellationToken.None);

        Assert.Null(reply);
        Assert.False(_handler.Invoked);
        var state = await user.Use<FollowStateEntityDataService>()
            .EntityGetAsync(s => s.ChannelId == "mp-1", CancellationToken.None);
        Assert.Null(state);
    }

    // ── 3. 记录在后（R3 §5.2——handler 回复优先，记录冲突不先中毒事务）──

    /// <summary>正常路径：handler 回复 + 事件事实落库 + 状态变化（编排层发布域事件）。</summary>
    [Fact]
    public async Task Orchestrate_Parsed_HandlerReply_RecordsFollow_PublishesEvent()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var occurred = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);
        _channel.Outcome = ChannelEventOutcome.Parsed(FollowEvent("openid-1", occurred));
        _handler.Reply = new EventReplyResult("欢迎关注", "text/xml");

        var reply = await user.Use<IEventOrchestrator>().OrchestrateAsync(
            "mp-1", "wechat_event", new ChannelInboundContext(new Dictionary<string, string?>()), CancellationToken.None);

        // 回复返回
        Assert.NotNull(reply);
        Assert.Equal("欢迎关注", reply!.Content);

        // 事件事实落库
        var evt = await user.Use<FollowEventEntityDataService>()
            .EntityGetAsync(e => e.ExternalUserId == "openid-1", CancellationToken.None);
        Assert.NotNull(evt);
        Assert.Equal("follow", evt!.EventType);

        // 状态行创建 + 域事件发布（仅状态变化——UId=uid-100 由编排层解析注入）
        var state = await user.Use<FollowStateEntityDataService>()
            .EntityGetAsync(s => s.ExternalUserId == "openid-1", CancellationToken.None);
        Assert.NotNull(state);
        Assert.True(state!.IsFollowing);
        var published = Assert.Single(_events.PublishedEvents);
        Assert.Equal("uid-100", Assert.IsType<FollowChangedEvent>(published).UId);
        Assert.True(Assert.IsType<FollowChangedEvent>(published).IsFollowing);
        Assert.Equal("follow", Assert.IsType<FollowChangedEvent>(published).EventType);
    }

    /// <summary>记录失败（DB 故障模拟——FollowState 门面异常）：回复照常（§5.3 失败隔离矩阵：记录失败 → 是回复 → 不发事件）。</summary>
    [Fact]
    public async Task Orchestrate_RecordFails_ReplyStillReturned_NoEvent()
    {
        using var _ = this;
        var occurred = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);
        _channel.Outcome = ChannelEventOutcome.Parsed(FollowEvent("openid-2", occurred));
        _handler.Reply = new EventReplyResult("欢迎关注2", "text/xml");

        // 替换 FollowState 门面为抛异常 Fake（记录失败模拟——编排 catch + 日志，回复照常；§5.1 第 6 步 try/catch）
        var (_, user) = BindTestScope(services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IFollowStateService>(_ => new ThrowingFollowStateService()));
        });

        var reply = await user.Use<IEventOrchestrator>().OrchestrateAsync(
            "mp-1", "wechat_event", new ChannelInboundContext(new Dictionary<string, string?>()), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal("欢迎关注2", reply!.Content);
        Assert.Empty(_events.PublishedEvents);   // 记录失败 → 不发域事件
    }

    // ── 4. 域事件仅状态变化发布（§3.7——重复投递不重复发）──

    /// <summary>重复投递（同 DedupeKey）：Recorded=false + StateChanged=false → 不发布域事件（幂等不噪音）。</summary>
    [Fact]
    public async Task Orchestrate_DuplicatePush_NoSecondEvent()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var occurred = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);
        var evt = FollowEvent("openid-3", occurred);

        var svc = user.Use<IEventOrchestrator>();
        _handler.Reply = new EventReplyResult("欢迎", "text/xml");
        _channel.Outcome = ChannelEventOutcome.Parsed(evt);
        await svc.OrchestrateAsync("mp-1", "wechat_event", new ChannelInboundContext(new Dictionary<string, string?>()), CancellationToken.None);
        _events.PublishedEvents.Clear();

        // 重复投递（同键）→ 状态无变化 → 不发布
        _channel.Outcome = ChannelEventOutcome.Parsed(evt);
        var reply = await svc.OrchestrateAsync("mp-1", "wechat_event", new ChannelInboundContext(new Dictionary<string, string?>()), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Empty(_events.PublishedEvents);

        // 事件行仍 1 条（幂等未二次写）
        var rows = await user.Use<FollowEventEntityDataService>()
            .EntitySelectAsync(e => e.ExternalUserId == "openid-3", ct: CancellationToken.None);
        Assert.Single(rows);
    }

    // ── 5. 乱序回落（§3.7——旧事件状态不动 → 不发事件）──

    /// <summary>乱序晚到旧事件：事件事实落库但状态不动（StateChanged=false）→ 不发布域事件。</summary>
    [Fact]
    public async Task Orchestrate_OutOfOrderOldEvent_NoEvent()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var later = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Unspecified);
        var earlier = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Unspecified);

        var svc = user.Use<IEventOrchestrator>();
        _channel.Outcome = ChannelEventOutcome.Parsed(FollowEvent("openid-4", later));
        await svc.OrchestrateAsync("mp-1", "wechat_event", new ChannelInboundContext(new Dictionary<string, string?>()), CancellationToken.None);
        _events.PublishedEvents.Clear();

        // 乱序旧 unfollow（更早时间）→ 状态保持关注 → 不发事件
        var staleUnfollow = new ChannelEvent
        {
            ChannelId = "mp-1",
            ChannelType = "wechat_event",
            ExternalUserId = "openid-4",
            EventType = "unfollow",
            OccurredAt = earlier,
            ReceivedAt = earlier.AddSeconds(1),
        };
        _channel.Outcome = ChannelEventOutcome.Parsed(staleUnfollow);
        await svc.OrchestrateAsync("mp-1", "wechat_event", new ChannelInboundContext(new Dictionary<string, string?>()), CancellationToken.None);

        Assert.Empty(_events.PublishedEvents);
        var state = await user.Use<FollowStateEntityDataService>()
            .EntityGetAsync(s => s.ExternalUserId == "openid-4", CancellationToken.None);
        Assert.True(state!.IsFollowing);
    }

    // ── 测试设施 ──

    private sealed class FakeEventChannel : ISsoChannel
    {
        public string ChannelType => "wechat_event";
        public string ChannelId => "mp-1";
        public ChannelEventOutcome Outcome { get; set; } = ChannelEventOutcome.NotApplicable;

        public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
            => Task.FromResult(new SsoChannelAuthResult(false, null, "NOT_IMPLEMENTED", 0));

        public Task<ChannelEventOutcome> HandleEventAsync(ChannelInboundContext context, CancellationToken ct = default)
            => Task.FromResult(Outcome);

        public Task<string?> VerifyUrlAsync(ChannelInboundContext context, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<string?> BuildAuthorizeUrlAsync(SsoChannelAuthorizeContext context, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    /// <summary>通道工厂 Fake（CreateAsync 直接返回 Fake 通道——编排时序测试聚焦点非通道构造）。</summary>
    private sealed class FakeFactory : ISsoChannelFactory
    {
        private readonly ISsoChannel _channel;
        public FakeFactory(ISsoChannel channel) => _channel = channel;

        public Task<ISsoChannel?> CreateAsync(string channelId, string? channelType = null, CancellationToken ct = default)
            => Task.FromResult<ISsoChannel?>(_channel);

        public Task<ISsoChannel?> CreateDefaultAsync(CancellationToken ct = default)
            => Task.FromResult<ISsoChannel?>(_channel);
    }

    /// <summary>事件回复 handler Fake（回复 first-non-null-wins 断言）。</summary>
    private sealed class FakeFollowChangedHandler : IEventReplyHandler
    {
        public string ChannelType => "wechat_event";
        public EventReplyResult? Reply { get; set; }
        public bool Invoked { get; private set; }

        public Task<EventReplyResult?> HandleAsync(ChannelEventDispatch dispatch, CancellationToken ct = default)
        {
            Invoked = true;
            return Task.FromResult(Reply);
        }
    }

    /// <summary>事件收集器——替代真实 ILocalEventBus，收集已派发事件供断言（对齐 Approval 测试先例）。</summary>
    private sealed class EventCollector : ILocalEventBus
    {
        public List<object> PublishedEvents { get; } = [];

        public Task PublishAsync<TEvent>(TEvent eventData) where TEvent : class
        {
            PublishedEvents.Add(eventData);
            return Task.CompletedTask;
        }

        public Task PostAsync<TEvent>(TEvent eventData) where TEvent : class
        {
            PublishedEvents.Add(eventData);
            return Task.CompletedTask;
        }

        public Task PublishAsync(Type eventType, object eventData)
        {
            PublishedEvents.Add(eventData);
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DispatchAsync(Type eventType, object eventData) => Task.CompletedTask;

        public IDisposable Subscribe<TEvent>(ILocalEventHandler<TEvent> handler) where TEvent : class
            => new NoopDisposable();
    }

    /// <summary>Noop 订阅句柄（EventCollector Subscribe 返回——对齐 Approval 先例）。</summary>
    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    /// <summary>uid 只读查询 Fake（openid → 固定 UId——编排层注入 FollowChangedEvent）。</summary>
    private sealed class FakeExternalIdService : IExternalIdentityService
    {
        private readonly string _uid;
        public FakeExternalIdService(string uid) => _uid = uid;

        public Task<ExternalIdentityDto?> GetByExternalIdAsync(string channelId, string externalUserId, CancellationToken ct = default)
            => Task.FromResult<ExternalIdentityDto?>(new ExternalIdentityDto(_uid, channelId, externalUserId, DateTime.UtcNow));

        public Task<IReadOnlyList<ExternalIdentityDto>> GetByAccountAsync(string uid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExternalIdentityDto>>([]);

        public Task<ExternalIdentityDto?> LinkAsync(string channelId, string externalUserId, string uid, string? extra = null, CancellationToken ct = default)
            => Task.FromResult<ExternalIdentityDto?>(new ExternalIdentityDto(uid, channelId, externalUserId, DateTime.UtcNow));

        public Task UnlinkAsync(string channelId, string externalUserId, string uid, string? extra = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>记录失败模拟（抛异常——编排第 6 步 try/catch 吞掉，回复照常）。</summary>
    private sealed class ThrowingFollowStateService : IFollowStateService
    {
        public Task<FollowRecordResult> RecordAsync(ChannelEvent evt, CancellationToken ct = default)
            => throw new InvalidOperationException("DB 故障模拟");

        public Task<FollowStateDto?> GetAsync(string channelId, string externalUserId, CancellationToken ct = default)
            => throw new InvalidOperationException();

        public Task<IReadOnlyList<FollowStateDto>> GetByExternalUserIdAsync(string externalUserId, CancellationToken ct = default)
            => throw new InvalidOperationException();

        public Task<IReadOnlyList<FollowDailyStatDto>> GetDailyStatsAsync(
            string channelId, DateTime fromUtc, DateTime toUtc, string? source = null, CancellationToken ct = default)
            => throw new InvalidOperationException();

        public Task<int> RebuildAsync(string channelId, CancellationToken ct = default)
            => throw new InvalidOperationException();

        public Task<int> PurgeEventsAsync(DateTime beforeUtc, int batchSize = 500, CancellationToken ct = default)
            => throw new InvalidOperationException();
    }
}
