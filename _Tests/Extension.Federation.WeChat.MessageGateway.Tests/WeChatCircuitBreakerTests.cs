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
