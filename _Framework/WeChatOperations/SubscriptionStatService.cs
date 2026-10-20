using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 订阅统计门面（I2-10 派生改造——写路径归编排层 <see cref="TKWF.Ext.Federation.IFollowStateService.RecordAsync"/>，
/// 本门面只读委托关注域日统计 <see cref="TKWF.Ext.Federation.IFollowStateService.GetDailyStatsAsync"/>；
/// 方案 §8.2 C9：统计口径与事件日志一致，删本地实体与双写）。
/// <para>消费方：<c>User.Use&lt;ISubscriptionStatService&gt;()</c>（AddConstructibleService 守卫工厂，帧内解析）。</para>
/// </summary>
public interface ISubscriptionStatService : IDomainService
{
    /// <summary>按日趋势（[from, to] 闭区间——委托 <see cref="TKWF.Ext.Federation.IFollowStateService.GetDailyStatsAsync"/>
    /// 日粒度分桶，返回 <see cref="SubscriptionStatPointDto"/>（Date/NewFollows/Unsubscribes/NetGrowth/Source——与
    /// <c>FollowDailyStatDto</c> 同形但不外露契约包类型，避免主包 API 签名穿透 Federation.Abstractions）；
    /// <paramref name="source"/> 可空 = 全来源合计 / 非空 = 仅该来源（对齐关注域 Source 维度）。
    /// <para>REST：<c>GET /api/wechat-ops/channels/{channelId}/subscription-stats?from=&amp;to=&amp;source=</c>；
    /// I2-10 响应体形状变化（entity 9 字段 → DTO 5 字段，字段名亦异）——SG3 客户端重生成。</para></summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(RestRoute = "/api/wechat-ops/channels/{channelId}/subscription-stats", RestMethod = "GET")]
    Task<IReadOnlyList<SubscriptionStatPointDto>> GetTrendAsync(string channelId, DateTime from, DateTime to, string? source = null, CancellationToken ct = default);

    /// <summary>
    /// 区间汇总统计（DMP-Lite 5.7 §2"统计数据"四指标卡）——[from, to] 闭区间按日聚合：
    /// 区间新增关注/区间取消关注/净增关注（<b>R7 负值按 0 计</b>——真实负值经
    /// <see cref="SubscriptionStatSummaryDto.NetCountRaw"/> 暴露 + <see cref="SubscriptionStatSummaryDto.IsNetNegative"/> 标记）/
    /// 累计增长率（以区间起始日累计净增为基数估算；期初累计 ≤ 0 不可判定——<see cref="SubscriptionStatSummaryDto.GrowthRate"/> 为 null）。
    /// <paramref name="source"/> 可空 = 全来源合计 / 非空 = 仅该来源（对齐 <see cref="GetTrendAsync"/> 过滤语义）。
    /// <para>REST：<c>GET /api/wechat-ops/channels/{channelId}/subscription-stats/summary?from=&amp;to=&amp;source=</c>。</para>
    /// </summary>
    [RequirePermission("WeChatOperations.Query")]
    [ApiExpose(RestRoute = "/api/wechat-ops/channels/{channelId}/subscription-stats/summary", RestMethod = "GET")]
    Task<SubscriptionStatSummaryDto> GetSummaryAsync(string channelId, DateTime from, DateTime to, string? source = null, CancellationToken ct = default);
}

/// <summary>
/// 订阅统计趋势点 DTO（I2-10 派生——<see cref="ISubscriptionStatService.GetTrendAsync"/> 返回元素；
/// 与 <see cref="TKWF.Ext.Federation.FollowDailyStatDto"/> 同形（Date/NewFollows/Unsubscribes/NetGrowth/Source），
/// <b>不直接外露契约包类型</b>——主包 API 签名不穿透 Federation.Abstractions（方案 §8.2 C9）。
/// </summary>
/// <param name="Date">统计日期（UTC 自然日——关注域按 <c>OccurredAt.Date</c> 分桶）。</param>
/// <param name="NewFollows">当日新增关注数（follow / scan_follow 事件计数）。</param>
/// <param name="Unsubscribes">当日取关数（unfollow 事件计数）。</param>
/// <param name="NetGrowth">当日净增（= NewFollows - Unsubscribes，可为负）。</param>
/// <param name="Source">来源/场景（null = 全来源合计；非空 = 该来源维度——对齐关注域 Source 列）。</param>
public sealed record SubscriptionStatPointDto(
    DateTime Date,
    int NewFollows,
    int Unsubscribes,
    int NetGrowth,
    string? Source);

/// <summary>
/// 订阅统计区间汇总（DMP-Lite 5.7 §2"统计数据"四指标卡——区间新增关注/区间取消关注/净增关注/累计增长率）。
/// <para><b>口径（R7）</b>：净增关注为负按 0 计（真实负值经 <see cref="NetCountRaw"/> 暴露 + <see cref="IsNetNegative"/>
/// 标记，前端可脚注口径）；累计增长率以区间起始日累计净增为基数估算（期初累计 ≤ 0 时不可判定——
/// <see cref="GrowthRate"/> 为 null + <see cref="GrowthRateUndeterminable"/>，UI 显示 "—"）。</para>
/// </summary>
/// <param name="TotalSubscribe">区间新增关注合计（闭区间 [from, to] 内 NewFollows 之和）。</param>
/// <param name="TotalUnsubscribe">区间取消关注合计（闭区间 [from, to] 内 Unsubscribes 之和）。</param>
/// <param name="NetCount">区间净增关注（显示口径——R7 负值按 0 计；真实值见 <see cref="NetCountRaw"/>）。</param>
/// <param name="NetCountRaw">区间净增关注原始值（= TotalSubscribe - TotalUnsubscribe，可为负——前端可脚注口径）。</param>
/// <param name="IsNetNegative">区间净增是否为负（true = 原始净增 &lt; 0，<see cref="NetCount"/> 已钳制为 0）。</param>
/// <param name="DailyAverageSubscribe">区间日均新增关注（TotalSubscribe ÷ 天数，闭区间含首尾）。</param>
/// <param name="DailyAverageUnsubscribe">区间日均取消关注（TotalUnsubscribe ÷ 天数，闭区间含首尾）。</param>
/// <param name="DayCount">区间天数（(to.Date - from.Date).Days + 1 含首尾；反序输入守卫 ≥ 1）。</param>
/// <param name="GrowthRate">累计增长率（估算——区间净增 ÷ 期初累计净增，四舍五入 4 位；期初累计 ≤ 0 不可判定为 null）。</param>
/// <param name="GrowthRateUndeterminable">累计增长率是否不可判定（期初累计净增 ≤ 0——<see cref="GrowthRate"/> 为 null，UI 显示 "—"）。</param>
public record SubscriptionStatSummaryDto(
    int TotalSubscribe,
    int TotalUnsubscribe,
    int NetCount,
    int NetCountRaw,
    bool IsNetNegative,
    double DailyAverageSubscribe,
    double DailyAverageUnsubscribe,
    int DayCount,
    double? GrowthRate,
    bool GrowthRateUndeterminable);

/// <inheritdoc />
[DiContractIgnore]
[GenerateController]
public sealed class SubscriptionStatService : DomainServiceBase, ISubscriptionStatService
{
    private IFollowStateService? _followStats;
    private IFollowStateService FollowStats => _followStats ??= User.Use<IFollowStateService>();

    public SubscriptionStatService(IDomainUser user) : base(user)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubscriptionStatPointDto>> GetTrendAsync(string channelId, DateTime from, DateTime to, string? source = null, CancellationToken ct = default)
    {
        // I2-10 派生：委托关注域日统计（GetDailyStatsAsync——统计口径与事件日志一致，零本地实体读）；
        //   闭区间 [from.Date, to.Date] → 半开 [from.Date, to.Date+1)（对齐 GetDailyStatsAsync 区间语义）
        var daily = await FollowStats.GetDailyStatsAsync(channelId, from.Date, to.Date.AddDays(1), source, ct);
        return daily
            .Select(p => new SubscriptionStatPointDto(p.Date, p.NewFollows, p.Unsubscribes, p.NetGrowth, p.Source))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<SubscriptionStatSummaryDto> GetSummaryAsync(string channelId, DateTime from, DateTime to, string? source = null, CancellationToken ct = default)
    {
        var startDate = from.Date;
        var endDate = to.Date;
        var endExclusive = endDate.AddDays(1);
        // 闭区间含首尾天数；反序输入（from > to）守卫 ≥ 1（日均除零防护）
        var dayCount = Math.Max(1, (endDate - startDate).Days + 1);

        // I2-10 派生：单次委托区间聚合（下界取 2000-01-01——期初累计净增基数；日粒度分桶内存可控，
        //   期初行（Date < startDate）与区间行（[startDate, endExclusive)）内存切分——口径不变）
        var daily = await FollowStats.GetDailyStatsAsync(channelId, PriorStartDate, endExclusive, source, ct);
        var window = daily.Where(p => p.Date >= startDate && p.Date < endExclusive).ToList();
        var prior = daily.Where(p => p.Date < startDate).ToList();

        var totalSubscribe = window.Sum(p => p.NewFollows);
        var totalUnsubscribe = window.Sum(p => p.Unsubscribes);
        var netCountRaw = totalSubscribe - totalUnsubscribe;
        var isNetNegative = netCountRaw < 0;
        var netCount = isNetNegative ? 0 : netCountRaw;   // R7：净增为负按 0 计（显示口径；真实负值经 NetCountRaw/IsNetNegative 暴露）

        // 累计增长率 = 区间净增 ÷ 期初累计净增（R7 口径——以区间起始日累计为基数估算）；期初累计 ≤ 0 → 不可判定（除零/负基数无意义）
        var priorNet = prior.Sum(p => p.NetGrowth);
        var growthRateUndeterminable = priorNet <= 0;
        double? growthRate = growthRateUndeterminable ? null : Math.Round((double)netCountRaw / priorNet, 4);

        return new SubscriptionStatSummaryDto(
            TotalSubscribe: totalSubscribe,
            TotalUnsubscribe: totalUnsubscribe,
            NetCount: netCount,
            NetCountRaw: netCountRaw,
            IsNetNegative: isNetNegative,
            DailyAverageSubscribe: (double)totalSubscribe / dayCount,
            DailyAverageUnsubscribe: (double)totalUnsubscribe / dayCount,
            DayCount: dayCount,
            GrowthRate: growthRate,
            GrowthRateUndeterminable: growthRateUndeterminable);
    }

    /// <summary>期初累计基线下界（2000-01-01 UTC——微信公众平台 2012 年才开放，安全下界；避免 DateTime.MinValue 跨年翻译风险）。</summary>
    private static readonly DateTime PriorStartDate = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
