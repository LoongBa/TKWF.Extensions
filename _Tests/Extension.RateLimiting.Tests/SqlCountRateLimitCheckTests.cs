using System;
using System.Threading.Tasks;

namespace TKWF.Ext.RateLimiting.Tests;

/// <summary>
/// SqlCountRateLimitCheck（DB 持久化 Provider）测试——fixed window 计数语义 + ADR89 CAS 并发原子性 + 读路径。
/// <para>方案 v0.3.0 §六验证清单：SqlCount 原子性（Oracle7 C9——进程内并发 Task：两并发 TryAcquire 同 key 同窗口 →
/// 恰一成功一失败，验证 ADR89 UpdateWhereAsync CAS 逻辑；跨实例正确性属 ORM/DB 契约，非测试可验证属性）。</para>
/// <para>测试走生产路径：真实 DI（Initializer + FreeSql SQLite）→ BindScope → <c>user.Use&lt;ITestRateLimitProbe&gt;()</c>
/// 帧内调用（守卫工厂经 CurrentAopUser 供给）；帧外解析抛守卫由 <see cref="Probe_OutsideFrame_ThrowsGuard"/> 锁定。</para>
/// </summary>
public class SqlCountRateLimitCheckTests
{
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    [Fact]
    public void TryAcquire_WithinLimit_ReturnsTrue_RemainingDecreases()
    {
        using var host = RateLimitTestHost.Create();

        Assert.True(host.Probe.TryAcquire("rl:test:user:u1", 5, FiveMinutes, out var remaining1));
        Assert.Equal(4, remaining1);

        Assert.True(host.Probe.TryAcquire("rl:test:user:u1", 5, FiveMinutes, out var remaining2));
        Assert.Equal(3, remaining2);
    }

    [Fact]
    public void TryAcquire_AtLimit_ReturnsFalse()
    {
        using var host = RateLimitTestHost.Create();
        var key = "rl:test:user:u-full";

        for (var i = 0; i < 5; i++)
            Assert.True(host.Probe.TryAcquire(key, 5, FiveMinutes, out _));

        // 第 6 次——fixed window 已满 → false + remaining=0
        Assert.False(host.Probe.TryAcquire(key, 5, FiveMinutes, out var remaining));
        Assert.Equal(0, remaining);
    }

    [Fact]
    public void TryAcquire_KeyIsolation_IndependentWindows()
    {
        using var host = RateLimitTestHost.Create();
        const int max = 2;

        Assert.True(host.Probe.TryAcquire("rl:test:user:a", max, FiveMinutes, out _));
        Assert.True(host.Probe.TryAcquire("rl:test:user:a", max, FiveMinutes, out _));
        Assert.False(host.Probe.TryAcquire("rl:test:user:a", max, FiveMinutes, out _));  // a 已满

        Assert.True(host.Probe.TryAcquire("rl:test:user:b", max, FiveMinutes, out _));   // b 独立——仍可尝试
    }

    [Fact]
    public void TryAcquire_WindowExpired_ResetsCount()
    {
        using var host = RateLimitTestHost.Create();
        var key = "rl:test:user:u-exp";
        var shortWindow = TimeSpan.FromMilliseconds(1200); // 窗口 1.2s

        Assert.True(host.Probe.TryAcquire(key, 2, shortWindow, out _));
        Assert.True(host.Probe.TryAcquire(key, 2, shortWindow, out _));
        Assert.False(host.Probe.TryAcquire(key, 2, shortWindow, out _));  // 满

        // 模拟时间流逝：直接操纵计数行 WindowEndUtc 过期（写 now-8h——补偿 SQLite UTC 读回偏移 +7h，
        // 保证 C# 侧 fresh.WindowEndUtc < now 判定命中；AuthSurface README 已知限制）
        host.Fsql.Update<RateLimitCounterEntity>()
            .Set(x => x.WindowEndUtc, DateTime.UtcNow.AddHours(-8))
            .Where(x => x.Key == key)
            .ExecuteAffrows();

        // 窗口过期 → 惰性重置（WindowEndUtc<now → Count=1 新窗口，本请求为重置者）→ 重试成功（新窗口第 1 次）
        Assert.True(host.Probe.TryAcquire(key, 2, shortWindow, out var remaining));
        Assert.Equal(1, remaining);
    }

    /// <summary>
    /// ADR89 CAS 并发原子性（Oracle7 C9）——两并发 TryAcquire 同 key 同窗口（max=1）→ 恰一成功一失败。
    /// <para>文件模式 SQLite（多连接共享库文件）+ Task.WhenAll 并行——每线程经 user.Use&lt;ITestRateLimitProbe&gt;()
    /// 独立 AsyncLocal 帧（CurrentAopUser 隔离）；CAS 单语句条件 UPDATE（行锁）保证单胜。</para>
    /// </summary>
    [Fact]
    public async Task TryAcquire_Concurrent_SingleWinner()
    {
        using var host = RateLimitTestHost.CreateFile(out _);
        var key = "rl:test:user:u-race";

        var results = await Task.WhenAll(
            Task.Run(() => AttemptAcquire(host, key, maxAttempts: 1)),
            Task.Run(() => AttemptAcquire(host, key, maxAttempts: 1)));

        // 恰一成功一失败（CAS：SET Count=Count+1 WHERE ... Count<1——单行锁串行化，败者 affected=0）
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(1, results.Count(r => !r));
    }

    [Fact]
    public void GetRemaining_NoKeyYet_ReturnsMaxAttempts()
    {
        using var host = RateLimitTestHost.Create();
        Assert.Equal(5, host.Probe.GetRemaining("rl:test:user:u-fresh", 5, FiveMinutes));
    }

    [Fact]
    public void GetRemaining_AfterAttempts_ReturnsRemaining()
    {
        using var host = RateLimitTestHost.Create();
        var key = "rl:test:user:u-rem";

        host.Probe.TryAcquire(key, 5, FiveMinutes, out _);
        host.Probe.TryAcquire(key, 5, FiveMinutes, out _);

        Assert.Equal(3, host.Probe.GetRemaining(key, 5, FiveMinutes));
    }

    [Fact]
    public void GetRetryAfter_NotLimited_ReturnsZero()
    {
        using var host = RateLimitTestHost.Create();
        Assert.Equal(TimeSpan.Zero, host.Probe.GetRetryAfter("rl:test:user:u-nolimit", FiveMinutes));
    }

    [Fact]
    public void GetRetryAfter_WindowFull_ReturnsPositive()
    {
        using var host = RateLimitTestHost.Create();
        var key = "rl:test:user:u-full-rta";

        for (var i = 0; i < 5; i++)
            host.Probe.TryAcquire(key, 5, FiveMinutes, out _);

        // fixed window：可重试时刻 = WindowEndUtc - now → 正
        Assert.True(host.Probe.GetRetryAfter(key, FiveMinutes) > TimeSpan.Zero);
    }

    [Fact]
    public void TryAcquire_InvalidArguments_ReturnsFalse()
    {
        using var host = RateLimitTestHost.Create();

        Assert.False(host.Probe.TryAcquire("", 5, FiveMinutes, out _));            // 空 key
        Assert.False(host.Probe.TryAcquire("rl:test:user:u-bad", 0, FiveMinutes, out _)); // max ≤ 0
        Assert.False(host.Probe.TryAcquire("rl:test:user:u-bad", 5, TimeSpan.Zero, out _)); // 窗口 ≤ 0
    }

    /// <summary>消费方确定性路径（RC1）：显式注册 SqlCountRateLimitCheck → 帧内解析链（Probe ctor 注入）即 SqlCount（非 Memory fallback）。</summary>
    [Fact]
    public void ExplicitRegistration_ResolvesSqlCount()
    {
        using var host = RateLimitTestHost.Create();
        Assert.Equal(typeof(SqlCountRateLimitCheck), host.Probe.CheckType);
    }

    /// <summary>帧外解析 IRateLimitCheck（裸 GetRequiredService）→ SqlCount ctor 解析守卫工厂门面 → 抛守卫（正确 fail）。</summary>
    [Fact]
    public void ExplicitRegistration_OutsideFrame_ThrowsGuard()
    {
        using var host = RateLimitTestHost.Create();

        // 未 Use 帧——SqlCount ctor 内 GetRequiredService<IRateLimitCounterDataService>（守卫工厂）→ CurrentAopUser 空 → 抛
        Assert.Throws<InvalidOperationException>(() => host.RateLimitCheck);
    }

    /// <summary>帧外守卫：无 User.Use 帧 → GetRequiredService&lt;IRateLimitCounterDataService&gt;()（守卫工厂）抛。</summary>
    [Fact]
    public void DataAccessFacade_OutsideFrame_ThrowsGuard()
    {
        using var host = RateLimitTestHost.Create();

        // 未 BindScope/未 Use 帧——CurrentAopUser 为空 → 守卫工厂抛 InvalidOperationException
        Assert.Throws<InvalidOperationException>(() => host.GetRequiredService<IRateLimitCounterDataService>());
    }

    private static bool AttemptAcquire(RateLimitTestHost host, string key, int maxAttempts)
        => host.Probe.TryAcquire(key, maxAttempts, FiveMinutes, out _);
}
