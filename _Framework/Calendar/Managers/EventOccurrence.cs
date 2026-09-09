using System;
using System.Collections.Generic;

namespace TKWF.Ext.Calendar
{
    /// <summary>
    /// 事件 occurrence——重复规则展开后的单次实例（查询时计算，不物化落库，D17）。
    /// <para>EndUtc 语义（P1/P2）：occurrence 起点 + 事件时长偏移（EndUtc - StartUtc）；AllDay 事件 +1 天（次日 00:00）。</para>
    /// </summary>
    /// <param name="StartUtc">occurrence 开始时间（UTC）。</param>
    /// <param name="EndUtc">occurrence 结束时间（UTC；null = 瞬时）。</param>
    /// <param name="EventId">来源事件 Id。</param>
    /// <param name="CalendarId">归属日历 Id。</param>
    /// <param name="Title">事件标题。</param>
    /// <param name="AllDay">全天标记。</param>
    public sealed record EventOccurrence(
        DateTime StartUtc,
        DateTime? EndUtc,
        long EventId,
        long CalendarId,
        string Title,
        bool AllDay);

    /// <summary>
    /// occurrence 查询结果（含显式截断标志——结果数恰等于 maxCount 不能区分截断/未截断，C4/D16）。
    /// </summary>
    /// <param name="Occurrences">按 StartUtc 升序（相等 + EventId 次级键）的 occurrence 列表；截断时为前 maxCount 个。</param>
    /// <param name="Truncated">true = 展开总数超过 maxCount 被截断（截断的是最晚 occurrence）。</param>
    public sealed record EventOccurrenceList(
        IReadOnlyList<EventOccurrence> Occurrences,
        bool Truncated);
}
