using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.RateLimiting;

/// <summary>
/// DB 持久化限流 Provider（跨实例正确）——<see cref="IRateLimitCheck"/> 的 <c>SqlCount</c> 实现。
/// <para>独立计数表 <see cref="RateLimitCounterEntity"/>（<c>TKWF_RateLimitCounter</c>）——fixed window 单窗口计数器；
/// TryAcquire 经 ADR89 <c>UpdateWhereAsync</c> CAS（<c>SET Count=Count+1 WHERE Key=@key AND WindowEndUtc&gt;=@now AND
/// Count&lt;@max</c>）——非读-改-写 TOCTOU，跨实例 DB 行锁正确（方案 v0.3.0 §五 R2 / ADR 决策 6）。</para>
/// <para>窗口语义（Oracle7 RC2）：<b>fixed window</b>（<c>WindowEndUtc</c> 过期重置）——与 Memory 默认的
/// <c>sliding window</c>（时间戳队列）行为差异：窗口边界允许双倍突发；MFA 验证等低频场景可接受。</para>
/// <para>⚠️ 已知限制（Oracle7 C6/RC3）：孤儿键（不再命中的 key）无自然回收、表增长无界——清理任务 v0.2.0；
/// 消费方应限制键空间为有界标识（如 userId）直至届时。</para>
/// <para><b>DI 形态（接线型，方案 RC1 排序裁定）</b>：消费方显式注册 <c>services.AddSingleton&lt;IRateLimitCheck,
/// SqlCountRateLimitCheck&gt;()</c>（确定性路径——依赖扩展自动注册序不可靠，TryAdd 首注册胜出下 fallback 可能静默取
/// Memory）。本类非 <see cref="TKW.Framework.Domain.DomainServiceBase"/>（<see cref="IRateLimitCheck"/> 非
/// <c>IDomainService</c>，AddConstructibleService 约束不满足）——数据访问经 <see cref="IRateLimitCounterDataService"/>
/// （AddConstructibleService 守卫工厂），<b>ctor 内解析并持有</b>（构造时机 = 消费门面经 <c>User.Use&lt;T&gt;()</c>
/// 守卫工厂解析依赖链内，CurrentAopUser 非空——守卫工厂供给 ctor IDomainUser）；门面方法执行（帧外）用持有实例，
/// 其内部 DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 直建（显式传 User，不依赖帧）。若消费方在帧外首次
/// 解析（如启动期裸 GetRequiredService）→ 守卫工厂抛领域架构守卫（正确 fail）。</para>
/// <para>契约为同步（<c>bool TryAcquire</c>）——DataService 为 async，内部 sync-over-async 桥接
/// （短 CAS 查询 + 低并发计数场景，方案 v0.3.0 已定契约形态）。</para>
/// </summary>
public sealed class SqlCountRateLimitCheck : IRateLimitCheck
{
    /// <summary>并发首插唯一约束冲突重试上限（A→B 过渡竞态窗口极窄，3 次兜底足够）。</summary>
    private const int InsertRetryLimit = 3;

    private readonly IRateLimitCounterDataService _data;
    private readonly ILogger<SqlCountRateLimitCheck>? _logger;

    /// <summary>
    /// ctor（接线型——仅 DI 可解析参数）。<b>帧内解析守卫工厂门面并持有</b>：构造时机 = 消费门面经
    /// <c>User.Use&lt;T&gt;()</c> 守卫工厂解析依赖链内（CurrentAopUser 非空——SqlCount 是 MfaService 等
    /// AddConstructibleService 门面的 ctor 依赖，解析发生在门面守卫工厂帧内）。
    /// </summary>
    public SqlCountRateLimitCheck(IServiceProvider serviceProvider, ILogger<SqlCountRateLimitCheck>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _logger = logger;
        // 帧内解析（构造链在外层 Use 帧内）——守卫工厂经 CurrentAopUser 供给 IDomainUser 创建
        // IRateLimitCounterDataService（DomainServiceBase）；门面方法执行（帧外）用持有实例（NoAop 直建不依赖帧）
        _data = serviceProvider.GetRequiredService<IRateLimitCounterDataService>();
    }

    /// <inheritdoc />
    public bool TryAcquire(string key, int maxAttempts, TimeSpan window, out int remaining)
    {
        remaining = 0;
        if (string.IsNullOrWhiteSpace(key) || maxAttempts <= 0 || window <= TimeSpan.Zero)
            return false; // fail-closed（契约入参守卫，不静默放行）

        var now = DateTime.UtcNow;
        var data = _data;

        // A. 首插：行不存在 → Count=1 + 新窗口（WindowStartUtc/WindowEndUtc 锚定）；并发首插唯一约束冲突 → 走 B 重试
        var existing = data.GetByKeyAsync(key, CancellationToken.None).GetAwaiter().GetResult();
        if (existing is null)
        {
            try
            {
                data.InsertAsync(new RateLimitCounterEntity
                {
                    Key = key,
                    Count = 1,
                    WindowStartUtc = now,
                    WindowEndUtc = now + window,
                }, CancellationToken.None).GetAwaiter().GetResult();
                remaining = Math.Max(0, maxAttempts - 1);
                return true;
            }
            catch (Exception ex) when (IsUniqueConstraint(ex))
            {
                // 并发首插败者——行已由他请求插入，走 B（CAS 递增）
                _logger?.LogDebug("SqlCountRateLimitCheck 并发首插冲突（Key={Key}）——转入 CAS 递增", key);
            }
        }

        // B. 窗口内 CAS 原子递增（ADR89 UpdateWhereAsync：SET Count=Count+1 WHERE Key=@key AND WindowEndUtc>=@now AND Count<@max）
        for (var attempt = 0; attempt < InsertRetryLimit; attempt++)
        {
            var affected = data.IncrementIfWithinWindowAsync(key, now, maxAttempts, CancellationToken.None).GetAwaiter().GetResult();
            if (affected == 1)
            {
                // 成功——重查行得剩余额度（max - Count，钳 0；计数已含本次）
                var row = data.GetByKeyAsync(key, CancellationToken.None).GetAwaiter().GetResult();
                remaining = Math.Max(0, maxAttempts - (row?.Count ?? 0));
                return true;
            }

            // affected == 0——读行分辩：窗口过期 → 重置重试；Count>=max → 超限拒绝；行缺失 → 重试
            var fresh = data.GetByKeyAsync(key, CancellationToken.None).GetAwaiter().GetResult();
            if (fresh is null)
                continue; // 并发首插败者/被清理——下一轮可能已可见

            if (fresh.WindowEndUtc < now)
            {
                // 窗口过期——惰性重置（CAS 守卫 WHERE WindowEndUtc<@now：仅首个重置者命中，防并发双重置竞态）
                var resetAffected = data.ResetWindowAsync(key, now, now + window, CancellationToken.None).GetAwaiter().GetResult();
                if (resetAffected == 1)
                {
                    // 本请求为重置者——重置语义 = 新窗口第 1 次尝试（Count=1，已含本次）
                    remaining = Math.Max(0, maxAttempts - 1);
                    return true;
                }
                continue; // 并发重置败者（他请求已重置）——重试递增
            }

            // 窗口未过期且递增失败 → Count >= max（超限）
            _logger?.LogDebug("SqlCountRateLimitCheck 超限拒绝（Key={Key}）", key);
            remaining = 0;
            return false;
        }

        // 兜底（理论不可达——首插冲突窗口 3 轮后仍行缺失）
        _logger?.LogWarning("SqlCountRateLimitCheck 重试耗尽（Key={Key}）——fail-closed 拒绝", key);
        remaining = 0;
        return false;
    }

    /// <inheritdoc />
    public TimeSpan GetRetryAfter(string key, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(key) || window <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var now = DateTime.UtcNow;
        var data = _data;
        var row = data.GetByKeyAsync(key, CancellationToken.None).GetAwaiter().GetResult();
        if (row is null)
            return TimeSpan.Zero;

        // fixed window 语义：可重试时刻 = 窗口结束时刻
        var retryAfter = row.WindowEndUtc - now;
        return retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter;
    }

    /// <inheritdoc />
    public int GetRemaining(string key, int maxAttempts, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(key) || maxAttempts <= 0)
            return 0;
        if (window <= TimeSpan.Zero)
            return 0; // fail-closed（契约 XML doc 注记：不静默返回"额度充足"误导信号）

        var data = _data;
        var row = data.GetByKeyAsync(key, CancellationToken.None).GetAwaiter().GetResult();
        if (row is null)
            return maxAttempts;
        return Math.Max(0, maxAttempts - row.Count);
    }

    /// <summary>唯一约束冲突探测（跨 ORM：SQLite/PG/MySql/SqlServer 消息签名；含 InnerException 展开）。</summary>
    private static bool IsUniqueConstraint(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var msg = e.Message;
            if (msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)      // SQLite
                || msg.Contains("unique_violation", StringComparison.OrdinalIgnoreCase)           // PostgreSQL 23505
                || msg.Contains("23505", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)            // MySql 1062
                || msg.Contains("Violation of UNIQUE KEY", StringComparison.OrdinalIgnoreCase)   // SqlServer 2627
                || msg.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
