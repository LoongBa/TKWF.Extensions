using System;

namespace TKWF.Ext.Tagging;

/// <summary>标签分析粒度（趋势聚合时间桶）。</summary>
public enum TagGranularity
{
    Hour = 0,
    Day = 1,
    Week = 2,
    Month = 3
}

/// <summary>标签频次聚合结果（高频标签 TopN）。</summary>
public record TagFrequency(string Dimension, string TagName, long Count);

/// <summary>标签趋势聚合点（时间桶内某标签命中数）。</summary>
public record TagTrendPoint(DateTime TimeBucket, string TagName, long Count);

/// <summary>维度分布聚合结果（维度命中占比）。</summary>
public record TagDimensionShare(string Dimension, long Count);

/// <summary>
/// 标签分析聚合辅助——时间粒度分桶键计算（TagHitRecordEntityDataService.GetTrendAsync 用）。
/// </summary>
public static class TaggingAggregations
{
    /// <summary>按粒度返回时间桶起点（UTC）：Hour→整点 / Day→当日零时 / Week→周一零时 / Month→当月一日。
    /// <para>Kind 处理：SQLite 读出 DateTime 为 Unspecified（存的是 UtcNow 写入的墙钟值）——Unspecified 视为已 UTC，
    /// 不做 ToUniversalTime 本地时区转换（否则 +8 时区桶偏移 8 小时）；仅 Local 显式转 UTC。</para></summary>
    public static DateTime BucketKey(DateTime time, TagGranularity granularity)
    {
        var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time;
        return granularity switch
        {
            TagGranularity.Hour => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc),
            TagGranularity.Day => new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc),
            TagGranularity.Week => BucketKey(DayOfWeekMonday(utc).Date, TagGranularity.Day),
            TagGranularity.Month => new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null)
        };
    }

    private static DateTime DayOfWeekMonday(DateTime utc)
    {
        var daysSinceMonday = ((int)utc.DayOfWeek + 6) % 7;   // Monday=0 ... Sunday=6
        return utc.AddDays(-daysSinceMonday);
    }
}