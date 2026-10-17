using System;
using System.Collections.Concurrent;

namespace TKWF.Federation.WeChat.MessageGateway;

/// <summary>
/// 熔断状态（三态机：关 → 开 → 半开 → 关，方案 §十·二 ②）。
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>关（正常放行）。</summary>
    Closed = 0,

    /// <summary>开（开窗期内拒绝转发——直接走本地兜底）。</summary>
    Open = 1,

    /// <summary>半开（窗口期满放行的单次探测中——成功回关 / 失败重置开窗）。</summary>
    HalfOpen = 2,
}

/// <summary>熔断状态快照（<see cref="WeChatCircuitBreaker.GetState"/> 返回——诊断/测试断言用）。</summary>
/// <param name="State">当前三态。</param>
/// <param name="ConsecutiveFailures">连续失败计数（开闸后清零；诊断用）。</param>
/// <param name="OpenUntilUtc">开窗期止点（UTC；非 Open 状态为 null）。</param>
public sealed record CircuitBreakerStateInfo(CircuitBreakerState State, int ConsecutiveFailures, DateTime? OpenUntilUtc);

/// <summary>
/// 微信消息转发熔断器（纯内存三态——方案 §十·二 ②：关→开→半开→关）。
/// <para>每 <c>EndpointId</c> 一个状态对象（<see cref="ConcurrentDictionary{TKey,TValue}"/> 隔离）；
/// <see cref="RecordFailure"/> 连续失败 ≥ <see cref="FailureThreshold"/>（默认 3）→ 开闸；
/// <see cref="RecordSuccess"/> → 回关 + 计数清零；<see cref="TryCall"/> → 开窗期（<see cref="OpenWindow"/>，默认 60s）
/// 内拒绝，窗口期满放行单次半开探测（成功 Close / 失败重置开窗）。</para>
/// <para>进程重启即清零（无持久化——对齐 <c>WeChatApiClient</c> 纯内存缓存先例）；多实例各计数为已知限制（方案 P2-1 显式声明）。</para>
/// </summary>
public sealed class WeChatCircuitBreaker
{
    private readonly ConcurrentDictionary<string, EndpointState> _states = new();

    /// <summary>开闸阈值：连续失败次数 ≥ 该值开闸（默认 3）。</summary>
    public int FailureThreshold { get; }

    /// <summary>开窗期：开闸后持续该时长拒绝转发（默认 60 秒），窗口期满放行单次半开探测。</summary>
    public TimeSpan OpenWindow { get; }

    /// <summary>
    /// 创建熔断器。
    /// </summary>
    /// <param name="failureThreshold">连续失败阈值（≥1，默认 3）。</param>
    /// <param name="openWindow">开窗期（必须为正，默认 60 秒）。</param>
    public WeChatCircuitBreaker(int failureThreshold = 3, TimeSpan? openWindow = null)
    {
        if (failureThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(failureThreshold), "熔断阈值必须 ≥ 1。");

        FailureThreshold = failureThreshold;
        OpenWindow = openWindow ?? TimeSpan.FromSeconds(60);
        if (OpenWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(openWindow), "开窗期必须为正。");
    }

    /// <summary>
    /// 记录一次失败：关态连续失败 ≥ 阈值 → 开闸（开窗期 = 本次时间 + OpenWindow，连续计数清零）；
    /// 半开探测失败 → 重置开窗（回开态，新开窗期）；开窗期内持续失败不改变状态。
    /// </summary>
    /// <param name="endpointId">下游端点 id（每个端点独立状态）。</param>
    /// <param name="utcNow">当前 UTC 时间（可注入控制——测试用；缺省系统时钟）。</param>
    public void RecordFailure(string endpointId, DateTime? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(endpointId);

        var state = GetOrCreate(endpointId);
        lock (state.Sync)
        {
            switch (state.State)
            {
                case CircuitState.Closed:
                    state.ConsecutiveFailures++;
                    if (state.ConsecutiveFailures >= FailureThreshold)
                        OpenCore(state, utcNow ?? DateTime.UtcNow);
                    break;

                case CircuitState.HalfOpen:
                    // 半开探测失败 → 重置开窗（新开窗期，回开态）
                    OpenCore(state, utcNow ?? DateTime.UtcNow);
                    break;

                case CircuitState.Open:
                    // 开窗期内持续失败：保持开态（不延长窗口——窗口期满照常探测）
                    break;
            }
        }
    }

    /// <summary>记录一次成功：回关 + 连续失败计数清零（任意状态下成功即回关）。</summary>
    /// <param name="endpointId">下游端点 id。</param>
    public void RecordSuccess(string endpointId)
    {
        ArgumentNullException.ThrowIfNull(endpointId);

        var state = GetOrCreate(endpointId);
        lock (state.Sync)
        {
            state.State = CircuitState.Closed;
            state.ConsecutiveFailures = 0;
            state.OpenUntilUtc = null;
        }
    }

    /// <summary>
    /// 尝试放行一次转发调用：
    /// <list type="bullet">
    /// <item>关（Closed）→ 放行（返回 true，状态不变）；</item>
    /// <item>开（Open）→ 窗口未到期拒绝（false）；窗口期满放行单次半开探测（转入 HalfOpen，返回 true）；</item>
    /// <item>半开（HalfOpen）→ 拒绝（false——单次探测唯一性，探测结果由 RecordSuccess/RecordFailure 落定）。</item>
    /// </list>
    /// </summary>
    /// <param name="endpointId">下游端点 id。</param>
    /// <param name="utcNow">当前 UTC 时间（注入时钟——测试控制开窗期）。</param>
    /// <returns>是否放行本次调用。</returns>
    public bool TryCall(string endpointId, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(endpointId);

        var state = GetOrCreate(endpointId);
        lock (state.Sync)
        {
            switch (state.State)
            {
                case CircuitState.Closed:
                    return true;

                case CircuitState.Open:
                    if (utcNow >= state.OpenUntilUtc)
                    {
                        // 窗口期满：放行单次半开探测
                        state.State = CircuitState.HalfOpen;
                        return true;
                    }
                    return false;

                case CircuitState.HalfOpen:
                    return false; // 单次探测唯一性——其余调用在探测落定前拒绝

                default:
                    return false;
            }
        }
    }

    /// <summary>当前熔断状态快照（诊断/测试断言）。</summary>
    /// <param name="endpointId">下游端点 id。</param>
    public CircuitBreakerStateInfo GetState(string endpointId)
    {
        ArgumentNullException.ThrowIfNull(endpointId);

        var state = GetOrCreate(endpointId);
        lock (state.Sync)
        {
            return new CircuitBreakerStateInfo(
                state.State switch
                {
                    CircuitState.Open => CircuitBreakerState.Open,
                    CircuitState.HalfOpen => CircuitBreakerState.HalfOpen,
                    _ => CircuitBreakerState.Closed,
                },
                state.ConsecutiveFailures,
                state.OpenUntilUtc);
        }
    }

    private EndpointState GetOrCreate(string endpointId)
        => _states.GetOrAdd(endpointId, static _ => new EndpointState());

    private void OpenCore(EndpointState state, DateTime utcNow)
    {
        state.State = CircuitState.Open;
        state.ConsecutiveFailures = 0;
        state.OpenUntilUtc = utcNow.Add(OpenWindow);
    }

    /// <summary>每端点独立状态对象（锁粒度 = 端点，互不阻塞）。</summary>
    private sealed class EndpointState
    {
        public object Sync { get; } = new();
        public CircuitState State { get; set; } = CircuitState.Closed;
        public int ConsecutiveFailures { get; set; }
        public DateTime? OpenUntilUtc { get; set; }
    }

    /// <summary>内部三态机状态（关→开→半开→关）。</summary>
    private enum CircuitState
    {
        Closed,
        Open,
        HalfOpen,
    }
}
