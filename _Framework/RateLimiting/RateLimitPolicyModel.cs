using System;

namespace TKWF.Ext.RateLimiting;

/// <summary>
/// 限流算法——对齐 <c>System.Threading.RateLimiting</c> 三种内置算法
/// （与主框架 Domain 层 <c>SlidingWindowRateLimiter</c> 同源）。
/// </summary>
public enum RateLimitAlgorithm
{
    /// <summary>固定窗口（FixedWindowRateLimiter）：窗口内许可数，窗口到期整体重置。</summary>
    FixedWindow,

    /// <summary>滑动窗口（SlidingWindowRateLimiter）：按分段平滑限流，旧分段过期逐步回收。</summary>
    SlidingWindow,

    /// <summary>令牌桶（TokenBucketRateLimiter）：突发许可采用令牌容量，按速率持续补充。</summary>
    TokenBucket
}

/// <summary>
/// 限流策略模型（对应 <c>System.Threading.RateLimiting</c> 三算法参数）。
/// <para>默认值对齐主框架 Domain 层 <see cref="TKW.Framework.Domain.RateLimiting.RateLimitPolicyOptions"/>
/// （PermitLimit=5 / Window=1min / SegmentsPerWindow=6 / QueueLimit=0）。</para>
/// </summary>
public sealed class RateLimitPolicyModel
{
    /// <summary>
    /// 限流算法（默认 <see cref="RateLimitAlgorithm.FixedWindow"/>）。
    /// </summary>
    public RateLimitAlgorithm Algorithm { get; set; } = RateLimitAlgorithm.FixedWindow;

    /// <summary>
    /// 许可数——固定/滑动窗口：窗口内允许的请求数；
    /// <b>令牌桶：TokenLimit 最大容量（突发许可上限），补充走 <see cref="ReplenishmentTokensPerSecond"/>
    /// （Oracle P2-1：PermitLimit 语义 = TokenLimit，非每秒补充量）</b>。默认 5（必须 &gt; 0）。
    /// </summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>窗口时长（秒）——固定/滑动窗口。默认 60（对齐主框架 1 分钟）。</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>滑动窗口分段数（越大限流越平滑），默认 6（对齐主框架 RateLimitPolicyOptions）。</summary>
    public int SegmentsPerWindow { get; set; } = 6;

    /// <summary>
    /// 令牌桶补充速率（个/秒，即 TokensPerPeriod/ReplenishmentPeriod 每秒 1 周期）。
    /// 默认 1。
    /// </summary>
    public int ReplenishmentTokensPerSecond { get; set; } = 1;

    /// <summary>排队许可数——默认 0（直接拒绝，不排队；对齐主框架 QueueLimit=0）。</summary>
    public int QueueLimit { get; set; } = 0;
}