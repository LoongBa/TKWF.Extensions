using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Interception.Auditing;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// FreeSql 审计日志存储实现——将 <see cref="AuditLogEntry"/> 映射为 <see cref="AuditLogEntity"/> 并持久化。
    /// <para>异常静默处理：写入失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class FreeSqlAuditLogStore : IAuditLogStore
    {
        private readonly IFreeSql _freeSql;
        private readonly ILogger<FreeSqlAuditLogStore> _logger;

        public FreeSqlAuditLogStore(IFreeSql freeSql, ILogger<FreeSqlAuditLogStore> logger)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SaveAsync(AuditLogEntry entry, CancellationToken ct = default)
        {
            if (entry == null) return;

            try
            {
                var entity = MapToEntity(entry);
                await _freeSql.Insert(entity).ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "审计日志写入失败: {ServiceName}.{MethodName}", entry.ServiceName, entry.MethodName);
            }
        }

        /// <summary>
        /// 将 <see cref="AuditLogEntry"/> 映射为 <see cref="AuditLogEntity"/>。
        /// </summary>
        private static AuditLogEntity MapToEntity(AuditLogEntry entry)
        {
            return new AuditLogEntity
            {
                UserName = entry.UserName,
                UserId = entry.UserId,
                ServiceName = entry.ServiceName,
                MethodName = entry.MethodName,
                ArgumentsJson = entry.ArgumentsJson,
                ExecutionTime = entry.ExecutionTime.DateTime,
                DurationMs = entry.DurationMs,
                Success = entry.Success,
                Exception = entry.Exception,
                CorrelationId = entry.CorrelationId,
                CreateTime = DateTimeOffset.Now
            };
        }
    }
}
