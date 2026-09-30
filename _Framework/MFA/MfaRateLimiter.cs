using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TKWF.Ext.MFA;

/// <summary>
/// 内存滑动窗口频控器（internal sealed）——按 key 独立窗口计数，并发安全。
/// <para>语义（方案 §四决策5 / Oracle C4/Q3）：per-(UserId, Method) 窗口在<b>单实例进程</b>内生效——
/// v0.1.0 为单实例频控，多实例部署不跨实例（跨实例共享需 DB 化，v0.2.0 候选）；SMS 发码频控多实例 =
/// 短信计费滥用真实风险（Oracle C8 明示单实例或外部限流器前置）。</para>
/// <para>⚠️ 承载于 Scoped 服务中的静态实例（<see cref="MfaService"/> / <see cref="SmsMfaMethod"/>）——
/// 频控须跨请求生效，静态字段保证进程级单例（不随 scope 重建）。键格式由调用方约定
/// （如 <c>mfa:verify:{userId}:{method}</c>、<c>mfa:sms:{userId}</c>）。</para>
/// </summary>
internal sealed class MfaRateLimiter
{
    /// <summary>key → 窗口内尝试时间戳队列（滑动窗口）。</summary>
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new();

    /// <summary>判定并计入一次尝试——窗口内计数 ≥ max → false（remaining=0）；未超限 → 记录本次尝试并返回剩余额度。</summary>
    public bool IsAllowed(string key, int maxAttempts, TimeSpan window, out int remaining)
    {
        remaining = 0;
        if (string.IsNullOrWhiteSpace(key) || maxAttempts <= 0 || window <= TimeSpan.Zero)
            return false;

        var now = DateTime.UtcNow;
        var cutoff = now - window;
        var queue = _windows.GetOrAdd(key, static _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() <= cutoff)
                queue.Dequeue();

            if (queue.Count >= maxAttempts)
                return false; // 窗口已满——remaining 恒 0（调用方经 GetRetryAfter 取可重试时间）

            queue.Enqueue(now);
            remaining = maxAttempts - queue.Count;
            return true;
        }
    }

    /// <summary>判定并计入一次尝试（简版）。</summary>
    public bool IsAllowed(string key, int maxAttempts, TimeSpan window)
        => IsAllowed(key, maxAttempts, window, out _);

    /// <summary>当前剩余额度（只读不计数）。</summary>
    public int GetRemaining(string key, int maxAttempts, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(key) || maxAttempts <= 0) return 0;

        var now = DateTime.UtcNow;
        var cutoff = now - window;
        if (!_windows.TryGetValue(key, out var queue))
            return maxAttempts;
        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() <= cutoff)
                queue.Dequeue();

            var remaining = Math.Max(0, maxAttempts - queue.Count);
            if (queue.Count == 0)
                _windows.TryRemove(key, out _); // 空队列回收——防 key 无界累积
            return remaining;
        }
    }

    /// <summary>窗口已满时下一次尝试可行的剩余等待时长（未满/键不存在 → Zero；供异常消息提示）。</summary>
    public TimeSpan GetRetryAfter(string key, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(key) || window <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var now = DateTime.UtcNow;
        if (!_windows.TryGetValue(key, out var queue))
            return TimeSpan.Zero;
        lock (queue)
        {
            var cutoff = now - window;
            while (queue.Count > 0 && queue.Peek() <= cutoff)
                queue.Dequeue();

            TimeSpan retryAfter;
            if (queue.Count == 0)
            {
                retryAfter = TimeSpan.Zero;
                _windows.TryRemove(key, out _);
            }
            else
            {
                retryAfter = queue.Peek() + window - now;  // 头部条目过期时刻 = 可重试时刻
                if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
            }
            return retryAfter;
        }
    }
}