using System;
using System.Collections.Generic;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志配置选项。
    /// <para>通过 <c>services.Configure&lt;AuditLoggingOptions&gt;(config.GetSection("TKWF:AuditLogging"))</c> 绑定。</para>
    /// </summary>
    public class AuditLoggingOptions
    {
        /// <summary>是否启用审计日志（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>是否为匿名调用记录审计日志（默认 false）。</summary>
        public bool LogAnonymous { get; set; } = false;

        /// <summary>是否序列化返回值到审计记录（默认 false，防止大对象/敏感返回值落盘）。</summary>
        public bool SaveReturnValues { get; set; } = false;

        /// <summary>附加敏感字段名集合（参数 JSON 序列化时值替换为 "***"）。</summary>
        public HashSet<string> AdditionalSensitiveFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 审计日志保留天数（V0.3.0 保留天数清理）——<see cref="AuditLogAnalyticsService.CleanupExpiredAsync"/>
        /// 删除 <c>ExecutionTime &lt; UtcNow - RetentionDays</c> 的过期记录（分批）。默认 90 天。
        /// <para>注意：该清理引入物理删除——限定 DataService <c>DeleteExpiredAsync</c> 单点物理删
        /// （hasSoftDelete:false，绝不走 EntitySoftDeleteAsync），不暴露管理端点。</para>
        /// </summary>
        public int RetentionDays { get; set; } = 90;

        /// <summary>
        /// 保留清理单批删除条数（V0.3.0）——分批删除防长事务/大锁（每批
        /// <see cref="AuditLogEntityDataService.DeleteExpiredAsync"/> 查询 Take 后删除，循环至清完）。默认 500。
        /// </summary>
        public int CleanupBatchSize { get; set; } = 500;
    }
}
