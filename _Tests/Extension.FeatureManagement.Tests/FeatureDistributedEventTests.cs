using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Events;
using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// v0.3.0 外部总线适配测试——D7-D9（[DistributedEvent] 特性 / 本地事件保留（Set/Delete 均发布 P5）/ 写失败不发布 /
/// 跨实例失效 handler bump 版本表 / 无总线降级（LocalDistributedEventBus 默认进程内）。
/// </summary>
public class FeatureDistributedEventTests
{
    private const string Theme = ConsumerFeatureContributor.StringFeature;
    private const string Checkout = ConsumerFeatureContributor.BooleanFeature;

    // ── D7 [DistributedEvent] 特性存在（反射断言） ──

    [Fact]
    public void Event_HasDistributedEventAttribute()
    {
        Assert.True(typeof(FeatureValueChangedEvent).IsDefined(typeof(DistributedEventAttribute), false));
    }

    // ── D7 本地事件发布保留（v0.2.0 语义——Set/Delete 均发布，P5） ──
    // 注：用每用例独立捕获 handler（非静态 TestFeatureChangedHandler.Received）——与 FeatureValueChangedEventTests
    // 并行执行时静态队列会跨类串扰（两用例同删 App.Theme/Global 事件交错）。

    [Fact]
    public async Task SetValue_PublishesLocalEvent()
    {
        using var capture = CreateWithLocalHandler();

        await capture.Host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);

        var evt = Assert.Single(capture.Received);
        Assert.Equal(Theme, evt.Name);
        Assert.Equal("dark", evt.NewValue);
    }

    [Fact]
    public async Task DeleteValue_PublishesLocalEvent()
    {
        using var capture = CreateWithLocalHandler();
        await capture.Host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);
        capture.Received.Clear();

        await capture.Host.Manager.DeleteValueAsync(Theme, FeatureProviders.Global, null, CancellationToken.None);

        var evt = Assert.Single(capture.Received);
        Assert.Equal(Theme, evt.Name);
        Assert.Equal("dark", evt.OldValue);
        Assert.Null(evt.NewValue);
    }

    [Fact]
    public async Task WriteValidationFailure_DoesNotPublishEvent()
    {
        using var capture = CreateWithLocalHandler();

        // v0.3.0 写时校验拒绝（Boolean 写 "abc" → ArgumentException 在写前）→ 不发布
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            capture.Host.Manager.SetValueAsync(Checkout, "abc", FeatureProviders.Global, null, CancellationToken.None));

        Assert.Empty(capture.Received);
    }

    // ── D8 跨实例失效：handler 收到远程事件 → 版本表 bump → 缓存立即新值 ──

    [Fact]
    public async Task RemoteEvent_BumpsVersion_ImmediateCacheRefresh()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Theme, "v1", FeatureProviders.Global, null, CancellationToken.None);
        var r1 = await host.Manager.GetValueAsync(Theme, null, null, CancellationToken.None);
        Assert.Equal("v1", r1);   // 缓存 v1

        var versionBefore = host.VersionRegistry.GetVersion(Theme);

        // 模拟远程实例：DB 直改（绕过本实例 Manager——另一实例的写路径）+ 远程事件经分布式总线到达
        await host.Fsql.Update<FeatureValueEntity>()
            .Set(e => e.Value, "v2")
            .Where(e => e.Name == Theme && e.ProviderName == FeatureProviders.Global)
            .ExecuteAffrowsAsync(CancellationToken.None);

        await host.DistributedFeatureChangedHandler.HandleEventAsync(
            new FeatureValueChangedEvent(Theme, FeatureProviders.Global, null, "v1", "v2"));

        // 版本表 bump（v1 → v2）——旧缓存 key 失效
        Assert.Equal(versionBefore + 1, host.VersionRegistry.GetVersion(Theme));

        // 缓存立即新值（重查 DB——跨实例即时生效）
        var r2 = await host.Manager.GetValueAsync(Theme, null, null, CancellationToken.None);
        Assert.Equal("v2", r2);
    }

    [Fact]
    public void DistributedHandler_IsPublicAndDiResolvable()
    {
        // C1：handler 必须 public（SG4 消费方生成 typeof(Handler) 引用——internal 无 IVT 时 CS0122）+ DI 可解析
        using var host = FeatureManagementTestHost.Create();
        Assert.True(typeof(DistributedFeatureChangedHandler).IsPublic);
        Assert.NotNull(host.DistributedFeatureChangedHandler);
    }

    // ── D9 无总线降级：LocalDistributedEventBus 默认进程内——写值正常 + 分布式 handler 经 LocalHandlerAdapter 收到事件 ──

    [Fact]
    public async Task LocalDistributedEventBus_DefaultMode_WriteReadWorks_NoException()
    {
        // 宿主默认已注册 LocalDistributedEventBus（模拟未接 RabbitMQ 的无总线模式）——写值/读值零异常（F8）
        using var host = FeatureManagementTestHost.Create();

        await host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);
        Assert.Equal("dark", await host.Manager.GetValueAsync(Theme, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task LocalDistributedEventBus_SubscribeHandler_ReceivesViaLocalHandlerAdapter()
    {
        // 默认无总线模式：分布式 handler 经 LocalDistributedEventBus.Subscribe → LocalHandlerAdapter → 本地总线派发
        using var host = FeatureManagementTestHost.Create();
        using var subscription = host.DistributedBus.Subscribe(host.DistributedFeatureChangedHandler);
        var versionBefore = host.VersionRegistry.GetVersion(Theme);

        await host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);

        // P1：默认模式双重递增（写路径 InvalidateCacheForName +1，handler 经 LocalHandlerAdapter bump +1）——功能无害
        Assert.Equal(versionBefore + 2, host.VersionRegistry.GetVersion(Theme));
        Assert.Equal("dark", await host.Manager.GetValueAsync(Theme, null, null, CancellationToken.None));
    }

    // ── 辅助 ──

    /// <summary>每用例独立捕获 handler（避开静态 TestFeatureChangedHandler.Received 跨类并行串扰）。</summary>
    private static LocalEventCapture CreateWithLocalHandler()
    {
        var received = new List<FeatureValueChangedEvent>();
        var host = FeatureManagementTestHost.Create(configure: services =>
            services.AddScoped<ILocalEventHandler<FeatureValueChangedEvent>>(_ => new CapturingLocalHandler(received)));
        return new LocalEventCapture(host, received);
    }

    private sealed class CapturingLocalHandler(List<FeatureValueChangedEvent> sink) : ILocalEventHandler<FeatureValueChangedEvent>
    {
        public Task HandleEventAsync(FeatureValueChangedEvent eventData)
        {
            sink.Add(eventData);
            return Task.CompletedTask;
        }
    }

    private sealed class LocalEventCapture(FeatureManagementTestHost host, List<FeatureValueChangedEvent> received) : IDisposable
    {
        public FeatureManagementTestHost Host { get; } = host;
        public List<FeatureValueChangedEvent> Received { get; } = received;
        public void Dispose() => Host.Dispose();
    }
}
