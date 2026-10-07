using System;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.RateLimiting;

/// <summary>
/// 限流计数表实体——点检查原语（<c>IRateLimitCheck</c>）的 DB 持久化 Provider 计数底册。
/// <para>SG1 化：声明式实体——partial + <c>[DomainGenerateCode]</c>。</para>
/// <para>语义（方案 v0.3.0 §五 R2 / ADR-RateLimiting 决策点 2）：fixed window 单窗口计数器——Key 唯一，
/// 窗口内 <c>Count</c> 经 ADR89 <c>UpdateWhereAsync</c> CAS 原子递增（<c>SET Count=Count+1 WHERE Key=@key AND
/// WindowEndUtc&gt;=@now AND Count&lt;@max</c>），跨实例 DB 行锁正确；首插靠 Key 唯一约束捕获并发冲突重试；
/// 惰性淘汰（过期行随下一次命中重置，无后台清理——孤儿键增长 v0.2.0 清理任务）。</para>
/// <para>表名/索引 <c>TKWF_</c>/<c>TKWFIX_</c> 前缀（ADR100 表名前缀批次 A 已闭环——新表直接带前缀）。</para>
/// </summary>
[Table("TKWF_RateLimitCounter")]
[DomainGenerateCode(DefaultPageSize = 50)]
[FreeSql.DataAnnotations.Index("TKWFIX_RateLimitCounter_Key", nameof(Key), IsUnique = true)]
public partial class RateLimitCounterEntity
{
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>计数键（<c>{policy}:{partitionBy}:{subject}</c> 规范化——Provider 视为不透明字符串，仅作计数键；唯一索引 TKWFIX_RateLimitCounter_Key 类级声明）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2, StringLength = 256)]
    public string Key { get; set; } = "";

    /// <summary>当前窗口内计数。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    public int Count { get; set; }

    /// <summary>窗口开始时刻（UTC）——语义锚定（GetRetryAfter 推导用）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    public DateTime WindowStartUtc { get; set; }

    /// <summary>窗口结束时刻（UTC）——窗口过期判定（<c>WindowEndUtc &lt; now</c> → 重置）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    public DateTime WindowEndUtc { get; set; }

    [FreeSql.DataAnnotations.Column(Position = 6, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    [FreeSql.DataAnnotations.Column(Position = 7)]
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
}
