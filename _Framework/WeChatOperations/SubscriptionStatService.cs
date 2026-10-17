using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 订阅统计门面（subscribe/unsubscribe 事件驱动——按 ChannelId 按日按来源 upsert 计数；设计文档 §3.3.1/§3.3.2）。
/// <para>唯一约束 (ChannelId, StatDate, Source)——每日每 channel 每来源一行（<b>汇总行 Source=null</b> + 各来源行，
/// V0.3.0 来源/场景维度 <see cref="WeChatSubscriptionSources"/>）；<see cref="RecordAsync"/> 按此键每日
/// 原子累加（查 → 更新或创建；唯一索引兜底并发 TOCTOU——败者重查转更新）。</para>
/// <para>消费方：<c>User.Use&lt;ISubscriptionStatService&gt;()</c>（AddConstructibleService 守卫工厂，帧内解析；
/// 事件 handler <c>WeChatOperationsReplyHandler</c> 帧内调用）。</para>
/// </summary>
public interface ISubscriptionStatService : IDomainService
{
    /// <summary>
    /// 记录订阅/取关计数（<paramref name="eventType"/>：1=subscribe / 2=unsubscribe）——每日 upsert：
    /// 按 (ChannelId, StatDate=UtcNow.Date, Source) 查 → 命中更新（对应列 +1 + NetCount 重算）/ 未命中创建。
    /// <paramref name="source"/> 可空 = 汇总行（不细分来源——存量调用零适配）；来源值见 <see cref="WeChatSubscriptionSources"/>。
    /// </summary>
    Task RecordAsync(string channelId, int eventType, string? source = null, CancellationToken ct = default);

    /// <summary>按日趋势（[from, to] 闭区间——ChannelId + StatDate 过滤；<paramref name="source"/> 可空 = 仅汇总行；
    /// 非空 = 仅该来源行；StatDate 升序）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<IReadOnlyList<SubscriptionStatEntity>> GetTrendAsync(string channelId, DateTime from, DateTime to, string? source = null, CancellationToken ct = default);

    /// <summary>
    /// 区间汇总统计（DMP-Lite 5.7 §2"统计数据"四指标卡）——[from, to] 闭区间按日聚合：
    /// 区间新增关注/区间取消关注/净增关注（<b>R7 负值按 0 计</b>——真实负值经
    /// <see cref="SubscriptionStatSummaryDto.NetCountRaw"/> 暴露 + <see cref="SubscriptionStatSummaryDto.IsNetNegative"/> 标记）/
    /// 累计增长率（以区间起始日累计净增为基数估算；期初累计 ≤ 0 不可判定——<see cref="SubscriptionStatSummaryDto.GrowthRate"/> 为 null）。
    /// <paramref name="source"/> 可空 = 仅汇总行；非空 = 仅该来源行（对齐 <see cref="GetTrendAsync"/> 过滤语义）。
    /// </summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<SubscriptionStatSummaryDto> GetSummaryAsync(string channelId, DateTime from, DateTime to, string? source = null, CancellationToken ct = default);
}

/// <summary>
/// 订阅统计区间汇总（DMP-Lite 5.7 §2"统计数据"四指标卡——区间新增关注/区间取消关注/净增关注/累计增长率）。
/// <para><b>口径（R7）</b>：净增关注为负按 0 计（真实负值经 <see cref="NetCountRaw"/> 暴露 + <see cref="IsNetNegative"/>
/// 标记，前端可脚注口径）；累计增长率以区间起始日累计净增为基数估算（期初累计 ≤ 0 时不可判定——
/// <see cref="GrowthRate"/> 为 null + <see cref="GrowthRateUndeterminable"/>，UI 显示 "—"）。</para>
/// </summary>
/// <param name="TotalSubscribe">区间新增关注合计（闭区间 [from, to] 内 SubscribeCount 之和）。</param>
/// <param name="TotalUnsubscribe">区间取消关注合计（闭区间 [from, to] 内 UnsubscribeCount 之和）。</param>
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
internal sealed class SubscriptionStatService : DomainServiceBase, ISubscriptionStatService
{
    private SubscriptionStatEntityDataService? _ds;
    private SubscriptionStatEntityDataService Ds => _ds ??= User.Use<SubscriptionStatEntityDataService>();

    public SubscriptionStatService(IDomainUser user) : base(user)
    {
    }

    /// <inheritdoc />
    public async Task RecordAsync(string channelId, int eventType, string? source = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return;
        if (eventType is not (1 or 2)) throw new ArgumentException("eventType 无效（1=subscribe / 2=unsubscribe）", nameof(eventType));

        var statDate = DateTime.UtcNow.Date;
        var nextDay = statDate.AddDays(1);
        // ⚠️ DateTime 等值比较在 FreeSql SQLite 下有 Kind/格式往返不一致风险（写入 Utc 午夜 vs 查询参数转本地/不同序列化
        //    ——upsert 查不到刚创建行撞唯一约束）；改用半开区间 [statDate, nextDay) 查当日行（范围语义无等值精度问题）
        // ⚠️ Source 可空（汇总行）——FreeSql null == null 语义：显式 IS NULL 分支（对齐 MessageGatewayService 先例）
        var existing = await Ds.EntityGetAsync(s => s.ChannelId == channelId && s.StatDate >= statDate && s.StatDate < nextDay
            && (source == null ? s.Source == null : s.Source == source), ct);
        if (existing is not null)
        {
            // 命中 → 原子更新（+1 + NetCount 重算）
            if (eventType == 1) existing.SubscribeCount++;
            else existing.UnsubscribeCount++;
            existing.NetCount = existing.SubscribeCount - existing.UnsubscribeCount;
            existing.UpdateTime = DateTime.UtcNow;
            await Ds.EntityUpdateAsync(existing, ct);
            return;
        }

        // 未命中 → 创建（并发唯一索引 UX 兜底——败者重查转更新）
        try
        {
            var entity = new SubscriptionStatEntity
            {
                ChannelId = channelId,
                StatDate = statDate,
                Source = source,
                SubscribeCount = eventType == 1 ? 1 : 0,
                UnsubscribeCount = eventType == 2 ? 1 : 0,
                NetCount = eventType == 1 ? 1 : -1,
                CreateTime = DateTime.UtcNow,
                UpdateTime = DateTime.UtcNow,
            };
            await Ds.EntityCreateAsync(entity, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 唯一冲突（并发双建）——败者重查转更新（对齐 AuthGrantCommandService 幂等 upsert 先例；
            //   半开区间范围查询——对齐 DateTime 等值往返一致性规避；Source 入键——显式 IS NULL 分支）
            var fresh = await Ds.EntityGetAsync(s => s.ChannelId == channelId && s.StatDate >= statDate && s.StatDate < nextDay
                && (source == null ? s.Source == null : s.Source == source), ct);
            if (fresh is null) throw;   // 非冲突异常——原样抛
            if (eventType == 1) fresh.SubscribeCount++;
            else fresh.UnsubscribeCount++;
            fresh.NetCount = fresh.SubscribeCount - fresh.UnsubscribeCount;
            fresh.UpdateTime = DateTime.UtcNow;
            await Ds.EntityUpdateAsync(fresh, ct);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubscriptionStatEntity>> GetTrendAsync(string channelId, DateTime from, DateTime to, string? source = null, CancellationToken ct = default)
        => await Ds.EntitySelectAsync(
            s => s.ChannelId == channelId && s.StatDate >= from.Date && s.StatDate <= to.Date
                && (source == null ? s.Source == null : s.Source == source),
            0, 1000,
            q => q.OrderBy(s => s.StatDate),
            ct);

    /// <inheritdoc />
    public async Task<SubscriptionStatSummaryDto> GetSummaryAsync(string channelId, DateTime from, DateTime to, string? source = null, CancellationToken ct = default)
    {
        var startDate = from.Date;
        var endDate = to.Date;
        var endExclusive = endDate.AddDays(1);
        // 闭区间含首尾天数；反序输入（from > to）守卫 ≥ 1（日均除零防护）
        var dayCount = Math.Max(1, (endDate - startDate).Days + 1);

        // 区间行（[from.Date, to.Date] 闭区间 = 半开 [startDate, to.Date+1 天)——对齐 RecordAsync 半开区间先例：
        //   FreeSql SQLite 内联 DateTime 无 Z 后缀 vs 存储带 Z（System.Data.SQLite）——闭区间上界 <= 在末日本身
        //   会字典序错位致最后一日本地漏计（GetTrendAsync 宽区间测试掩盖、此处区间精确聚合必须规避）；
        //   Source 可空显式 IS NULL 分支）
        var rows = await Ds.EntitySelectAsync(
            s => s.ChannelId == channelId && s.StatDate >= startDate && s.StatDate < endExclusive
                && (source == null ? s.Source == null : s.Source == source),
            0, 1000,
            q => q.OrderBy(s => s.StatDate),
            ct);

        var totalSubscribe = rows.Sum(s => s.SubscribeCount);
        var totalUnsubscribe = rows.Sum(s => s.UnsubscribeCount);
        var netCountRaw = totalSubscribe - totalUnsubscribe;
        var isNetNegative = netCountRaw < 0;
        var netCount = isNetNegative ? 0 : netCountRaw;   // R7：净增为负按 0 计（显示口径；真实负值经 NetCountRaw/IsNetNegative 暴露）

        // 期初累计净增（StatDate < from.Date——累计增长率基数；同 source/IS NULL 过滤）
        // ⚠️ 期初天数无上界（开区间）——limit 放宽至 100_000（对齐 AuditLogging 内存聚合先例），防长运行期基线截断致增长率失真
        var priorRows = await Ds.EntitySelectAsync(
            s => s.ChannelId == channelId && s.StatDate < startDate
                && (source == null ? s.Source == null : s.Source == source),
            0, 100_000, null, ct);
        var priorNet = priorRows.Sum(s => s.NetCount);

        // 累计增长率 = 区间净增 ÷ 期初累计净增（R7 口径——以区间起始日累计为基数估算）；期初累计 ≤ 0 → 不可判定（除零/负基数无意义）
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
}