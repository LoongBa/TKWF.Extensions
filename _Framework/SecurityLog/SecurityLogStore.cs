using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志存储实现——经 <see cref="SecurityLogEntityDataService"/>（SG1/xCodeGen 生成的 DataService）
    /// 委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para><b>只增不改（Oracle C2）</b>：本实现仅调用 <c>EntityCreateAsync</c>（追加写），
    /// 无任何 Update/Delete 路径——安全日志不可篡改语义。</para>
    /// <para>异常静默处理：落库失败时记录 Warning 日志，不抛出异常（审计不阻断认证流程）。</para>
    /// </summary>
    internal sealed class SecurityLogStore : ISecurityLogStore
    {
        private readonly SecurityLogEntityDataService _dataService;
        private readonly ILogger<SecurityLogStore> _logger;

        public SecurityLogStore(SecurityLogEntityDataService dataService, ILogger<SecurityLogStore> logger)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task SaveAsync(SecurityLogEntry entry, CancellationToken ct = default)
        {
            if (entry == null) return;

            try
            {
                var entity = MapToEntity(entry);
                await _dataService.EntityCreateAsync(entity, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "安全日志写入失败: {EventType} 用户 {UserName}（不阻断认证流程）",
                    entry.EventType, entry.UserName);
            }
        }

        private static SecurityLogEntity MapToEntity(SecurityLogEntry entry)
        {
            return new SecurityLogEntity
            {
                EventType = entry.EventType,
                EventCategory = entry.EventCategory,
                UserName = entry.UserName,
                UserId = entry.UserId,
                IpAddress = entry.IpAddress,
                UserAgent = entry.UserAgent,
                Result = entry.Result,
                Detail = entry.Detail,
                CorrelationId = entry.CorrelationId,
                CreateTime = DateTime.UtcNow
            };
        }
    }
}
