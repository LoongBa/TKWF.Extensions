using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Interception.Auditing;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志存储实现——经 <see cref="AuditLogEntityDataService"/>（SG1/xCodeGen 生成的 DataService）
    /// 委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class AuditLogStore : IAuditLogStore
    {
        private readonly AuditLogEntityDataService _dataService;
        private readonly ILogger<AuditLogStore> _logger;

        public AuditLogStore(AuditLogEntityDataService dataService, ILogger<AuditLogStore> logger)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SaveAsync(AuditLogEntry entry, CancellationToken ct = default)
        {
            if (entry == null) return;

            try
            {
                var entity = MapToEntity(entry);
                await _dataService.EntityCreateAsync(entity, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "审计日志写入失败: {ServiceName}.{MethodName}", entry.ServiceName, entry.MethodName);
            }
        }

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