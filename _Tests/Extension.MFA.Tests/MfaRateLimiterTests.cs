using System;
using TKWF.Ext.MFA;

namespace TKWF.Ext.MFA.Tests;

/// <summary>
/// 内存滑动窗口频控器单元测试（internal MfaRateLimiter——MFA csproj 已配 <c>InternalsVisibleTo</c>，IVT 直测）。
/// <para>语义（方案 §四决策5 / Oracle C4/Q3）：per-key 窗口内计数——未超限记录并返回剩余额度；超限 false；
/// 并发安全（<c>ConcurrentDictionary</c> + 每 key 队列锁）。尝试频控/发送频控均经此原语。</para>
/// </summary>
public class MfaRateLimiterTests
{
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    [Fact]
    public void IsAllowed_WithinLimit_ReturnsTrue_RemainingDecreases()
    {
        var limiter = new MfaRateLimiter();

        Assert.True(limiter.IsAllowed("k", maxAttempts: 5, FiveMinutes, out var remaining1));
        Assert.Equal(4, remaining1);

        Assert.True(limiter.IsAllowed("k", 5, FiveMinutes, out var remaining2));
        Assert.Equal(3, remaining2);
    }

    [Fact]
    public void IsAllowed_AtLimit_ReturnsFalse()
    {
        var limiter = new MfaRateLimiter();

        for (var i = 0; i < 5; i++)
            Assert.True(limiter.IsAllowed("k", maxAttempts: 5, FiveMinutes));

        // 第 6 次——窗口已满 → false
        Assert.False(limiter.IsAllowed("k", maxAttempts: 5, FiveMinutes, out var remaining));
        Assert.Equal(0, remaining);
    }

    [Fact]
    public void IsAllowed_KeyIsolation_IndependentWindows()
    {
        var limiter = new MfaRateLimiter();
        const int max = 2;

        Assert.True(limiter.IsAllowed("a", max, FiveMinutes));
        Assert.True(limiter.IsAllowed("a", max, FiveMinutes));
        Assert.False(limiter.IsAllowed("a", max, FiveMinutes));   // a 已满

        Assert.True(limiter.IsAllowed("b", max, FiveMinutes));    // b 独立——仍可尝试
    }

    [Fact]
    public void GetRemaining_NoKeyYet_ReturnsMaxAttempts()
    {
        var limiter = new MfaRateLimiter();

        Assert.Equal(5, limiter.GetRemaining("fresh", maxAttempts: 5, FiveMinutes));
    }

    [Fact]
    public void GetRemaining_AfterAttempts_ReturnsRemaining()
    {
        var limiter = new MfaRateLimiter();
        limiter.IsAllowed("k", 5, FiveMinutes);
        limiter.IsAllowed("k", 5, FiveMinutes);

        Assert.Equal(3, limiter.GetRemaining("k", 5, FiveMinutes));
    }

    [Fact]
    public void GetRetryAfter_NotLimited_ReturnsZero()
    {
        var limiter = new MfaRateLimiter();

        Assert.Equal(TimeSpan.Zero, limiter.GetRetryAfter("k", FiveMinutes));
    }

    [Fact]
    public void GetRetryAfter_WindowFull_ReturnsPositive()
    {
        var limiter = new MfaRateLimiter();
        for (var i = 0; i < 5; i++)
            limiter.IsAllowed("k", maxAttempts: 5, FiveMinutes);

        var retryAfter = limiter.GetRetryAfter("k", FiveMinutes);

        // 头部条目过期时刻 = 可重试时刻——须为正（滑动窗口未到）
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void IsAllowed_InvalidArguments_ReturnsFalse()
    {
        var limiter = new MfaRateLimiter();

        Assert.False(limiter.IsAllowed("", 5, FiveMinutes));          // 空 key
        Assert.False(limiter.IsAllowed("k", 0, FiveMinutes));         // max ≤ 0
        Assert.False(limiter.IsAllowed("k", 5, TimeSpan.Zero));       // 窗口 ≤ 0
    }
}
