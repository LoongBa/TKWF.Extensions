using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Captcha;

/// <summary>
/// 挑战态（方案 §5.2——答案 SHA256 散列 + 尝试上限 + 有效期；明文答案永不落挑战态/载荷）。
/// </summary>
/// <param name="AnswerHash">答案 SHA256 hex（小写，<c>AnswerHashAsync</c> 统一出口；比对经 CryptographicOperations.FixedTimeEquals）。</param>
/// <param name="AttemptsRemaining">剩余可答错次数（签发表 = AttemptsPerChallenge；耗尽即作废）。</param>
/// <param name="ExpireAt">有效期截止（UTC 绝对时刻——MemoryCache AbsoluteExpiration 与业务判定同源）。</param>
internal sealed record CaptchaChallengeState(string AnswerHash, int AttemptsRemaining, DateTime ExpireAt);

/// <summary>
/// 挑战态存储（v1 in-memory；方案 §5.2/风险 #2）——<see cref="IMemoryCache"/> 键 <c>captcha:challenge:{challengeId}</c>
/// （TTL 5min 配置化）+ 单挑战答错上限 + 验证成功即消费。
/// <para>⚠️ <b>in-memory CAS 非原子</b>：并发 Verify 对同一挑战的读-改-写可能互相覆盖（如并发答错与并发消费）。
/// 单实例内自加锁——按 challengeId 的 <see cref="SemaphoreSlim"/> 串行化写路径（<see cref="ExecuteAtomicAsync"/>），
/// 并发消费/并发答错/消费与答错交错全部安全；锁表保存在内部 <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// （实例级——本 Store 为 DI 单例，跨门面/Provider 实例共享同一锁表）。</para>
/// <para>局限（写入使用指南）：多实例部署各实例独立缓存 + 独立锁表——挑战只在签发实例可验（第三方无状态
/// Provider 可水平扩展）；DB 化留 v1.1。锁条目随挑战移除释放，未再访问的过期挑战锁残留受缓存容量约束（可接受）。</para>
/// </summary>
internal sealed class CaptchaChallengeStateStore
{
    private const string KeyPrefix = "captcha:challenge:";

    private readonly IMemoryCache _cache;
    private readonly ILogger<CaptchaChallengeStateStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public CaptchaChallengeStateStore(IMemoryCache cache, ILogger<CaptchaChallengeStateStore> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>写入挑战态（绝对过期 = 状态自身 ExpireAt——TTL 单一真相源）。</summary>
    public void Set(string challengeId, CaptchaChallengeState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(challengeId);
        _cache.Set(Key(challengeId), state, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = new DateTimeOffset(state.ExpireAt, TimeSpan.Zero)
        });
        _logger.LogDebug("挑战态已写入（challengeId={ChallengeIdPrefix}，TTL 至 {ExpireAt:O}）", challengeId[..Math.Min(8, challengeId.Length)], state.ExpireAt);
    }

    /// <summary>
    /// 原子读-改-写（per-challengeId 自锁）：<paramref name="update"/> 在锁内执行，接收当前状态
    /// （null = 不存在/已过期/轻量移除），返回下一状态（null = 移除作废；非 null = 回写且过期时刻不变）。
    /// </summary>
    public async Task<CaptchaChallengeState?> ExecuteAtomicAsync(string challengeId, Func<CaptchaChallengeState?, CaptchaChallengeState?> update, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(challengeId);

        var gate = _locks.GetOrAdd(challengeId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var key = Key(challengeId);
            var current = _cache.Get<CaptchaChallengeState>(key);
            var next = update(current);
            if (next is null)
            {
                _cache.Remove(key);
                DropLock(challengeId);
                return null;
            }

            _cache.Set(key, next, new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = new DateTimeOffset(next.ExpireAt, TimeSpan.Zero)
            });
            return next;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string Key(string challengeId) => KeyPrefix + challengeId;

    /// <summary>移除已作废挑战的锁条目（释放 SemaphoreSlim 资源；并发窗口内新请求会重建新锁——不串旧锁）。</summary>
    private void DropLock(string challengeId)
    {
        if (_locks.TryRemove(challengeId, out var gate))
            gate.Dispose();
    }
}