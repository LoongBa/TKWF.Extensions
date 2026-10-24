using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// <see cref="EventOrchestrator"/> 排他单模型装配守卫测试（v0.12.0——fail-fast）：
/// 同一 ChannelType 至多一个<b>业务</b> handler；诊断（<see cref="DiagnosticEventReplyHandlerAttribute"/>）
/// 与通道绑定（<see cref="IChannelSpecificEventReplyHandler"/>）不计数。
/// <para>生产路径宿主（对齐 <see cref="EventOrchestratorI2Tests"/>）：FederationExtensionInitializer 真实 DI +
/// 双 handler 注册 → <c>User.Use&lt;IEventOrchestrator&gt;()</c> 守卫工厂构造时跑 ValidateExclusivity。</para>
/// </summary>
public sealed class EventOrchestratorExclusivityTests : TestHostBase
{
    protected override TestUserInfo CreateUser() => new("exclusivity-user", "排他守卫测试用户");

    /// <summary>双业务 handler 同 ChannelType → 构造抛 InvalidOperationException（配置即错，fail-fast）。</summary>
    [Fact]
    public void TwoBusinessHandlers_SameChannelType_Throws()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
        {
            new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);
            FederationTestHost.RegisterEntityDacs(services);
            services.AddScoped<IEventReplyHandler>(_ => new FakeBizHandler());
            services.AddScoped<IEventReplyHandler>(_ => new FakeBizHandler());
        });

        Assert.Throws<InvalidOperationException>(() => user.Use<IEventOrchestrator>());
    }

    /// <summary>一个业务 + 一个诊断（带 <see cref="DiagnosticEventReplyHandlerAttribute"/>）→ 不抛
    /// （诊断 handler 不参与业务计数——探针/诊断可共存）。</summary>
    [Fact]
    public void BusinessPlusDiagnostic_DoesNotThrow()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
        {
            new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);
            FederationTestHost.RegisterEntityDacs(services);
            services.AddScoped<IEventReplyHandler>(_ => new FakeBizHandler());
            services.AddScoped<IEventReplyHandler>(_ => new FakeDiagnosticHandler());
        });

        // 不应抛——单业务 handler + 诊断可正常解析编排器
        var orchestrator = user.Use<IEventOrchestrator>();
        Assert.NotNull(orchestrator);
    }

    /// <summary>一个业务 + 一个通道绑定（<see cref="IChannelSpecificEventReplyHandler"/>）→ 不抛
    /// （W3 通道绑定桶是精确覆盖，非默认链——不参与类型桶业务计数）。</summary>
    [Fact]
    public void BusinessPlusChannelSpecific_DoesNotThrow()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
        {
            new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);
            FederationTestHost.RegisterEntityDacs(services);
            services.AddScoped<IEventReplyHandler>(_ => new FakeBizHandler());
            services.AddScoped<IEventReplyHandler>(_ => new FakeChannelSpecificHandler("mp-specific"));
        });

        var orchestrator = user.Use<IEventOrchestrator>();
        Assert.NotNull(orchestrator);
    }

    /// <summary>业务 handler（ChannelType=wechat_event——占位实现）。</summary>
    private sealed class FakeBizHandler : IEventReplyHandler
    {
        public string ChannelType => "wechat_event";

        public Task<EventReplyResult?> HandleAsync(ChannelEventDispatch dispatch, CancellationToken ct = default)
            => Task.FromResult<EventReplyResult?>(null);
    }

    /// <summary>诊断 handler（[DiagnosticEventReplyHandler]——不计入业务计数）。</summary>
    [DiagnosticEventReplyHandler]
    private sealed class FakeDiagnosticHandler : IEventReplyHandler
    {
        public string ChannelType => "wechat_event";

        public Task<EventReplyResult?> HandleAsync(ChannelEventDispatch dispatch, CancellationToken ct = default)
            => Task.FromResult<EventReplyResult?>(null);
    }

    /// <summary>通道绑定 handler（IChannelSpecificEventReplyHandler——精确覆盖非默认链，不计入类型桶业务计数）。</summary>
    private sealed class FakeChannelSpecificHandler : IEventReplyHandler, IChannelSpecificEventReplyHandler
    {
        private readonly string _name;
        public FakeChannelSpecificHandler(string name) => _name = name;

        public string ChannelType => "wechat_event";

        public string ChannelId => _name;

        public Task<EventReplyResult?> HandleAsync(ChannelEventDispatch dispatch, CancellationToken ct = default)
            => Task.FromResult<EventReplyResult?>(null);
    }
}
