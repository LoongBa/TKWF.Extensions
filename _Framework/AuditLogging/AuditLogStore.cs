using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Events;
using TKW.Framework.Domain.Interception.Auditing;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志存储实现——经 <see cref="AuditLogEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>接线型（ADR90）：<see cref="IAuditLogStore"/> 是主框架契约（非 IDomainService），主框架 AuditLogFilterAttribute
    /// 经 <c>context.ServiceProvider.GetService&lt;IAuditLogStore&gt;()</c> 普通 DI 解析——ctor(IServiceProvider, ILogger)
    /// 全 DI 可解析，DataService 经 IServiceProvider C1 延迟解析。</para>
    /// <para>失败信号：诊断计数 + 同步 <see cref="IAuditFailureHook"/>（异常隔离）；事件默认关（<see cref="AuditEventPublishingOptions.PublishEvents"/>）。</para>
    /// </summary>
    internal sealed class AuditLogStore : IAuditLogStore
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AuditLogStore> _logger;

        private AuditLogEntityDataService? _dataService;
        private AuditLogEntityDataService DataService => _dataService ??= _serviceProvider.GetRequiredService<AuditLogEntityDataService>();

        private bool _resolved;
        private IAuditFailureHook? _failureHook;
        private AuditDiagnostics? _diagnostics;
        private ILocalEventBus? _eventBus;
        private IAuditMaskingPolicy? _masking;
        private bool _publishEvents;

        public AuditLogStore(IServiceProvider serviceProvider, ILogger<AuditLogStore> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SaveAsync(AuditLogEntry entry, CancellationToken ct = default)
        {
            if (entry == null) return;

            EnsureResolved();

            try
            {
                var entity = MapToEntity(entry);
                await DataService.EntityCreateAsync(entity, ct);

                if (_publishEvents && _eventBus is not null)
                {
                    await SafePublishAsync(new AuditLogPersisted(
                        entity.Id, entry.ExecutionTime, entry.ServiceName, entry.MethodName,
                        entry.Success, entry.DurationMs, entry.CorrelationId));
                }
            }
            catch (Exception ex)
            {
                _diagnostics?.IncrementWriteFailures();
                RaiseFailure(entry, ex);

                _logger.LogWarning(ex, "审计日志写入失败: {ServiceName}.{MethodName}", entry.ServiceName, entry.MethodName);

                if (_publishEvents && _eventBus is not null)
                {
                    await SafePublishAsync(new AuditLogPersistFailed(
                        entry.ServiceName, entry.MethodName, entry.ExecutionTime,
                        ex.GetType().Name, entry.CorrelationId));
                }
            }
        }

        private void EnsureResolved()
        {
            if (_resolved) return;
            _resolved = true;

            _failureHook = _serviceProvider.GetService<IAuditFailureHook>();
            _diagnostics = _serviceProvider.GetService<AuditDiagnostics>();
            _masking = _serviceProvider.GetService<IAuditMaskingPolicy>();
            _publishEvents = _serviceProvider.GetService<IOptions<AuditEventPublishingOptions>>()?.Value.PublishEvents ?? false;

            if (_publishEvents)
            {
                _eventBus = _serviceProvider.GetService<ILocalEventBus>();
            }
        }

        private void RaiseFailure(AuditLogEntry entry, Exception ex)
        {
            if (_failureHook is null) return;

            try
            {
                _failureHook.OnPersistFailed(new AuditPersistFailure(
                    AuditFacet.Invocation,
                    DateTimeOffset.UtcNow,
                    entry.ServiceName,
                    entry.MethodName,
                    entry.CorrelationId,
                    ex.GetType().Name));
            }
            catch (Exception hookEx)
            {
                _logger.LogWarning(hookEx, "审计失败回调异常（已隔离，不影响审计写入/业务调用）");
            }
        }

        private async Task SafePublishAsync<TEvent>(TEvent eventData) where TEvent : class
        {
            try
            {
                await _eventBus!.PublishAsync(eventData);
                _diagnostics?.IncrementEventsPublished();
            }
            catch (Exception ex)
            {
                _diagnostics?.IncrementEventPublishFailures();
                _logger.LogWarning(ex, "审计事件发布失败（已隔离，不影响审计写入/业务调用）");
            }
        }

        private AuditLogEntity MapToEntity(AuditLogEntry entry)
        {
            return new AuditLogEntity
            {
                UserName = entry.UserName,
                UserId = entry.UserId,
                ServiceName = entry.ServiceName,
                MethodName = entry.MethodName,
                ArgumentsJson = _masking is null ? entry.ArgumentsJson : _masking.MaskText(entry.ArgumentsJson),
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
