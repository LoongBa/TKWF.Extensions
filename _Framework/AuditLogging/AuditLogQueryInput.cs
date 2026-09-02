using System;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志查询输入参数——所有过滤字段可选，支持任意组合。
    /// <para>分页使用 <see cref="Skip"/> + <see cref="Take"/>（FreeSql 天然支持）。
    /// <see cref="Take"/> 默认 50（对齐 DefaultPageSize），最大 200（防滥用）。</para>
    /// </summary>
    public record AuditLogQueryInput
    {
        /// <summary>执行时间范围起始（含）。null = 不限。</summary>
        public DateTime? StartTime { get; init; }

        /// <summary>执行时间范围结束（含）。null = 不限。</summary>
        public DateTime? EndTime { get; init; }

        /// <summary>调用者用户名（LIKE '%value%' 模糊匹配）。null = 不限。</summary>
        public string? UserName { get; init; }

        /// <summary>调用者用户 ID（精确匹配）。null = 不限。</summary>
        public string? UserId { get; init; }

        /// <summary>目标服务名（精确匹配）。null = 不限。</summary>
        public string? ServiceName { get; init; }

        /// <summary>目标方法名（精确匹配）。null = 不限。</summary>
        public string? MethodName { get; init; }

        /// <summary>是否执行成功（精确匹配）。null = 不限。</summary>
        public bool? Success { get; init; }

        /// <summary>关联 ID（精确匹配）。null = 不限。</summary>
        public string? CorrelationId { get; init; }

        /// <summary>最小执行耗时（毫秒，含）。null = 不限。</summary>
        public int? MinDurationMs { get; init; }

        /// <summary>最大执行耗时（毫秒，含）。null = 不限。</summary>
        public int? MaxDurationMs { get; init; }

        /// <summary>
        /// 跳过前 N 条记录（分页偏移量）。默认 0。
        /// </summary>
        public int Skip { get; init; }

        /// <summary>
        /// 返回记录数上限。默认 50，最大 200（防滥用）。
        /// </summary>
        public int Take { get; init; } = 50;
    }
}
