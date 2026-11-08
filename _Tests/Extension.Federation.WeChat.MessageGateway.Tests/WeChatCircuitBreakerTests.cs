using System;
using TKWF.Federation.WeChat.MessageGateway;

namespace TKWF.Federation.WeChat.MessageGateway.Tests;

/// <summary>
/// <see cref="WeChatCircuitBreaker"/> 熔断状态机单测——纯内存直测（注入时钟控制开窗期）。
/// 覆盖：连续失败开闸 / 半开探测成功回关 / 半开探测失败重置开窗 / 独立计数 / 阈值可配 / 成功清零。
/// </summary>
public class WeChatCircuitBreakerTests
{
    private const string EpA = "endpoint-a";
    private const string EpB = "endpoint-b";
    private static readonly DateTime T0 = new(2026, 10, 18, 8, 0, 0, DateTimeKind.Utc);

    private static WeChatCircuitBreaker Create(int threshold = 3, TimeSpan? openWindow = null)
        => new(threshold, openWindow);

    // ---------- 初态与放行 ----------

    [Fact]
    public void TryCall_FreshBreaker_Closed_Allows()
    {
        var breaker = Create();

        Assert.True(breaker.TryCall(EpA, T0));
        Assert.Equal(CircuitBreakerState.Closed, breaker.GetState(EpA).State);
    }

    // ---------- 连续失败开闸 ----------

    [Fact]
    public void RecordFailure_ConsecutiveBelowThreshold_StaysClosed()
    {
        var breaker = Create(threshold: 3);

        breaker.RecordFailure(EpA, T0);
        breaker.RecordFailure(EpA, T0);

        Assert.True(breaker.TryCall(EpA, T0));
        Assert.Equal(CircuitBreakerState.Closed, breaker.GetState(EpA).State);
    }

    [Fact]
    public void RecordFailure_ReachesThreshold_Opens()
    {
        var breaker = Create(threshold: 3);

        breaker.RecordFailure(EpA, T0);
        breaker.RecordFailure(EpA, T0);
        breaker.RecordFailure(EpA, T0);

        Assert.False(breaker.TryCall(EpA, T0)); // 开闸 → 拒绝
        Assert.Equal(CircuitBreakerState.Open, breaker.GetState(EpA).State);
    }

    [Fact]
    public void RecordFailure_CustomThreshold_Respected()
    {
        var breaker = Create(threshold: 2);

        breaker.RecordFailure(EpA, T0);
        Assert.True(breaker.TryCall(EpA, T0));

        breaker.RecordFailure(EpA, T0);
        Assert.False(breaker.TryCall(EpA, T0)); // 2 次即开闸
    }

    // ---------- 开窗期拒绝与期满半开探测 ----------

    [Fact]
    public void TryCall_OpenWithinWindow_Rejects()
    {
        var breaker = Create(openWindow: TimeSpan.FromSeconds(60));
        OpenWith(breaker, EpA, T0);

        Assert.False(breaker.TryCall(EpA, T0.AddSeconds(30)));
        Assert.False(breaker.TryCall(EpA, T0.AddSeconds(59)));
    }

    [Fact]
    public void RecordFailure_OpenState_SustainedFailure_DoesNotExtendWindow()
    {
        var breaker = Create(openWindow: TimeSpan.FromSeconds(60));
        OpenWith(breaker, EpA, T0);

        Assert.Equal(T0.AddSeconds(60), breaker.GetState(EpA).OpenUntilUtc); // 开闸止点基线 = 开闸时刻 + 开窗期

        // 开窗期内持续失败 → 不延长窗口（止点仍为原开闸时刻 + 开窗期，窗口期满照常探测）
        breaker.RecordFailure(EpA, T0.AddSeconds(10));
        breaker.RecordFailure(EpA, T0.AddSeconds(30));
        breaker.RecordFailure(EpA, T0.AddSeconds(59));

        Assert.Equal(CircuitBreakerState.Open, breaker.GetState(EpA).State);
        Assert.Equal(T0.AddSeconds(60), breaker.GetState(EpA).OpenUntilUtc);
    }

    [Fact]
    public void TryCall_WindowExpired_AllowsSingleProbe_HalfOpen()
    {
        var breaker = Create(openWindow: TimeSpan.FromSeconds(60));
        OpenWith(breaker, EpA, T0);

        Assert.True(breaker.TryCall(EpA, T0.AddSeconds(60)));  // 窗口期满 → 放行单次半开探测
        Assert.Equal(CircuitBreakerState.HalfOpen, breaker.GetState(EpA).State);

        Assert.False(breaker.TryCall(EpA, T0.AddSeconds(60))); // 探测在飞 → 拒绝（单次探测唯一性）
    }

    // ---------- 半开探测成功回关 ----------

    [Fact]
    public void HalfOpenProbe_Success_ClosesAndResetsCounter()
    {
        var breaker = Create(threshold: 3, openWindow: TimeSpan.FromSeconds(60));
        OpenWith(breaker, EpA, T0);

        Assert.True(breaker.TryCall(EpA, T0.AddSeconds(60))); // 探测放行
        breaker.RecordSuccess(EpA);                          // 探测成功 → 回关

        Assert.Equal(CircuitBreakerState.Closed, breaker.GetState(EpA).State);
        Assert.True(breaker.TryCall(EpA, T0.AddSeconds(60)));

        // 计数已清零：再失败 2 次仍不触发（阈值 3 需连续 3 次）
        breaker.RecordFailure(EpA, T0.AddSeconds(60));
        breaker.RecordFailure(EpA, T0.AddSeconds(60));
        Assert.True(breaker.TryCall(EpA, T0.AddSeconds(60)));
    }

    // ---------- 半开探测失败重置开窗 ----------

    [Fact]
    public void HalfOpenProbe_Failure_ResetsOpenWindow()
    {
        var breaker = Create(openWindow: TimeSpan.FromSeconds(60));
        OpenWith(breaker, EpA, T0);

        Assert.True(breaker.TryCall(EpA, T0.AddSeconds(60)));          // 探测放行（半开）
        breaker.RecordFailure(EpA, T0.AddSeconds(60));                // 探测失败 → 重置开窗（新窗口止点 = 探测时刻 + 60s）

        Assert.Equal(CircuitBreakerState.Open, breaker.GetState(EpA).State);
        Assert.False(breaker.TryCall(EpA, T0.AddSeconds(60)));        // 原窗口期满点已失效 → 仍拒绝

        Assert.True(breaker.TryCall(EpA, T0.AddSeconds(120)));        // 新窗口期满 → 再次放行探测
    }

    // ---------- 独立计数 ----------

    [Fact]
    public void FailureCounting_PerEndpoint_Isolated()
    {
        var breaker = Create(threshold: 3);

        breaker.RecordFailure(EpA, T0);
        breaker.RecordFailure(EpA, T0);
        breaker.RecordFailure(EpA, T0); // A 开闸

        Assert.False(breaker.TryCall(EpA, T0));
        Assert.True(breaker.TryCall(EpB, T0));   // B 不受影响
        Assert.Equal(CircuitBreakerState.Closed, breaker.GetState(EpB).State);
    }

    [Fact]
    public void RecordSuccess_ClosesAndZeroes_EvenAfterOpen()
    {
        var breaker = Create(threshold: 2);
        OpenWith(breaker, EpA, T0);

        breaker.RecordSuccess(EpA);

        Assert.Equal(CircuitBreakerState.Closed, breaker.GetState(EpA).State);
        Assert.True(breaker.TryCall(EpA, T0));
    }

    // ---------- 并发压力（锁粒度=端点，不变式不被并发破坏） ----------

    [Fact]
    public void Concurrent_RecordFailure_ThresholdExact_Opens_SingleWindowEnd()
    {
        var breaker = Create(threshold: 3, openWindow: TimeSpan.FromSeconds(60)); // 阈值 3 精确触发

        // 并发失败 100 次（远超阈值）——不变式：① 阈值精确触发开闸 ② 窗口止点不被并发写坏（不延窗）
        Parallel.For(0, 100, _ => breaker.RecordFailure(EpA, T0));

        Assert.Equal(CircuitBreakerState.Open, breaker.GetState(EpA).State);
        Assert.Equal(T0.AddSeconds(60), breaker.GetState(EpA).OpenUntilUtc);
        Assert.False(breaker.TryCall(EpA, T0)); // 开闸期内拒绝
    }

    [Fact]
    public void Concurrent_TryCall_WindowExpired_SingleProbe_OnlyOneAllowed()
    {
        var breaker = Create(openWindow: TimeSpan.FromSeconds(60));
        OpenWith(breaker, EpA, T0);

        var now = T0.AddSeconds(60); // 窗口期满（并发同刻放行竞争）
        var allowed = 0;
        Parallel.For(0, 32, _ =>
        {
            if (breaker.TryCall(EpA, now))
                Interlocked.Increment(ref allowed);
        });

        Assert.Equal(1, allowed); // 半开单探测唯一——并发不多个放行
        Assert.Equal(CircuitBreakerState.HalfOpen, breaker.GetState(EpA).State);
    }

    // ---------- 参数校验 ----------

    [Fact]
    public void Ctor_InvalidThreshold_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeChatCircuitBreaker(failureThreshold: 0));
    }

    [Fact]
    public void Ctor_NonPositiveWindow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeChatCircuitBreaker(openWindow: TimeSpan.Zero));
    }

    /// <summary>连败到开闸态（次数 = 熔断器阈值）。</summary>
    private static void OpenWith(WeChatCircuitBreaker breaker, string endpointId, DateTime at)
    {
        for (var i = 0; i < breaker.FailureThreshold; i++)
            breaker.RecordFailure(endpointId, at);
    }
}
