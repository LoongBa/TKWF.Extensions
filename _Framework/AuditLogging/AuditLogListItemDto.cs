using System;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志列表项 DTO——从 <see cref="AuditLogEntity"/> 投影，不含 <c>ArgumentsJson</c>（安全决策 D5：防止敏感参数结构泄露）。
    /// </summary>
    public record AuditLogListItemDto
    {
        /// <summary>主键。</summary>
        public long Id { get; init; }

        /// <summary>调用者用户名。</summary>
        public string? UserName { get; init; }

        /// <summary>调用者用户 ID。</summary>
        public string? UserId { get; init; }

        /// <summary>目标服务名（类名）。</summary>
        public string ServiceName { get; init; } = "";

        /// <summary>目标方法名。</summary>
        public string MethodName { get; init; } = "";

        /// <summary>执行时间（DateTime，存储时丢偏移量）。</summary>
        public DateTime ExecutionTime { get; init; }

        /// <summary>执行耗时（毫秒）。</summary>
        public int DurationMs { get; init; }

        /// <summary>是否执行成功。</summary>
        public bool Success { get; init; }

        /// <summary>异常信息（失败时记录）。</summary>
        public string? Exception { get; init; }

        /// <summary>关联 ID（分布式链路追踪）。</summary>
        public string? CorrelationId { get; init; }

        /// <summary>记录创建时间。</summary>
        public DateTimeOffset CreateTime { get; init; }
    }
}
