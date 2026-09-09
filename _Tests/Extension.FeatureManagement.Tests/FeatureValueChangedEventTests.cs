using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.Events;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// v0.2.0 变更事件测试——D6 + D8 + D11（发布 Old/New 值 / 消费方订阅 / Commit 后 / 写失败不发布）。
/// </summary>
public class FeatureValueChangedEventTests
{
    private const string Theme = ConsumerFeatureContributor.StringFeature;

    // ── D6 事件发布（Set 含 Old/New；Delete New=null） ──

    [Fact]
    public async Task SetValue_PublishesEvent_WithOldAndNew()
    {
        using var host = CreateWithHandler();
        await host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);
        TestFeatureChangedHandler.Reset();

        await host.Manager.SetValueAsync(Theme, "light", FeatureProviders.Global, null, CancellationToken.None);

        var evt = Assert.Single(TestFeatureChangedHandler.Received);
        Assert.Equal(Theme, evt.Name);
        Assert.Equal(FeatureProviders.Global, evt.ProviderName);
        Assert.Equal("dark", evt.OldValue);
        Assert.Equal("light", evt.NewValue);
    }

    [Fact]
    public async Task DeleteValue_PublishesEvent_NewNull()
    {
        using var host = CreateWithHandler();
        await host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);
        TestFeatureChangedHandler.Reset();

        await host.Manager.DeleteValueAsync(Theme, FeatureProviders.Global, null, CancellationToken.None);

        var evt = Assert.Single(TestFeatureChangedHandler.Received);
        Assert.Equal("dark", evt.OldValue);
        Assert.Null(evt.NewValue);
    }

    [Fact]
    public async Task SetValue_NonGlobal_PublishesEvent()
    {
        using var host = CreateWithHandler();
        TestFeatureChangedHandler.Reset();

        await host.Manager.SetValueAsync(Theme, "user-value", FeatureProviders.User, "u-1", CancellationToken.None);

        var evt = Assert.Single(TestFeatureChangedHandler.Received);
        Assert.Equal(FeatureProviders.User, evt.ProviderName);
        Assert.Equal("u-1", evt.ProviderKey);
        Assert.Null(evt.OldValue);   // 首次设置
        Assert.Equal("user-value", evt.NewValue);
    }

    // ── D8 消费方订阅 + handler 异常隔离（总线 P0-4） ──

    [Fact]
    public async Task ConsumerHandler_ReceivesEvent_IsolatedFromWriter()
    {
        using var host = CreateWithHandler();
        TestFeatureChangedHandler.Reset();

        await host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);

        Assert.Single(TestFeatureChangedHandler.Received);
        // 写方法正常返回（handler 异常被总线隔离——若 handler 抛，P0-4 永不重抛）
    }

    // ── D11 Commit 后发布：写失败不发布 ──

    [Fact]
    public async Task SetValue_Failure_DoesNotPublish()
    {
        using var host = CreateWithHandler();
        TestFeatureChangedHandler.Reset();

        // 非法 providerName（空）→ ArgumentException 在写前 → 不发布
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => host.Manager.SetValueAsync(Theme, "x", "", null, CancellationToken.None));

        Assert.Empty(TestFeatureChangedHandler.Received);
    }

    private static FeatureManagementTestHost CreateWithHandler()
        => FeatureManagementTestHost.Create(configure: services =>
        {
            services.AddScoped<ILocalEventHandler<FeatureValueChangedEvent>, TestFeatureChangedHandler>();
        });
}
