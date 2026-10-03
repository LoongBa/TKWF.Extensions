using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Interception.Auditing;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志存储实现——经 <see cref="AuditLogEntityDataService"/>（SG1/xCodeGen 生成的 DataService）
    /// 委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// <para>V4.10.53（领域自治根治，ADR90）：<b>接线型</b>（skill §4.2/§4.7-7）——<see cref="IAuditLogStore"/>
    /// 是<b>主框架契约</b>（非 IDomainService，不可修改主框架），主框架 <c>AuditLogFilterAttribute</c> 经
    /// <c>context.ServiceProvider.GetService&lt;IAuditLogStore&gt;()</c> <b>普通 DI</b> 解析——旧 ctor 注入
    /// IDomainUser（永不注册 DI）致 GetService 构造失败、审计落库静默失效（真实生产故障）；改
    /// ctor(IServiceProvider, ILogger) 全 DI 可解析 → GetService 可构造、审计落库生效。
    /// DataService 经 <see cref="IServiceProvider"/> <c>GetRequiredService&lt;T&gt;()</c> C1 延迟解析（对齐
    /// FreeSqlAccountLockoutPolicy/LoginHistoryService 接线型先例——接线型边界不吞守卫语义）。</para>
    /// </summary>
    internal sealed class AuditLogStore : IAuditLogStore
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AuditLogStore> _logger;

        private AuditLogEntityDataService? _dataService;
        private AuditLogEntityDataService DataService => _dataService ??= _serviceProvider.GetRequiredService<AuditLogEntityDataService>();

        public AuditLogStore(IServiceProvider serviceProvider, ILogger<AuditLogStore> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SaveAsync(AuditLogEntry entry, CancellationToken ct = default)
        {
            if (entry == null) return;

            try
            {
                var entity = MapToEntity(entry);
                await DataService.EntityCreateAsync(entity, ct);
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