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
    /// 调用审计写入器——实现主框架 <see cref="IAuditLogStore"/> 契约的**接线型**组件（非 IDomainService）。
    /// <para>
    /// 形态（V0.6.x 统一 Writer 范式，对齐 <see cref="BusinessAuditWriter"/>）：构造注入基础设施
    /// （masking / failure hook / diagnostics / options），DataService 经 <see cref="IServiceProvider"/> 延迟解析；
    /// 不注入 <see cref="TKW.Framework.Domain.Interfaces.IDomainUser"/>（永不注册 DI），不直接使用
    /// <c>IFreeSql</c> / <c>IEntityDAC</c>（数据访问红线）。
    /// </para>
    /// <para>
    /// 写入语义：脱敏后落库；成功后可发布 <c>AuditLogPersisted</c>；失败恒触发
    /// <see cref="IAuditFailureHook"/> + 诊断计数，并按 <see cref="AuditFailurePolicyOptions.FailOpen"/>
    /// 决定吞/抛（默认 fail-open）。事件默认关（<see cref="AuditEventPublishingOptions.PublishEvents"/>）。
    /// </para>
    /// </summary>
    internal sealed class AuditLogWriter : IAuditLogStore
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AuditLogWriter> _logger;
        private readonly IAuditMaskingPolicy _masking;
        private readonly IAuditFailureHook _failureHook;
        private readonly AuditDiagnostics _diagnostics;
        private readonly bool _failOpen;
        private readonly bool _publishEvents;

        private AuditLogEntityDataService? _dataService;
        private ILocalEventBus? _eventBus;

        private AuditLogEntityDataService DataService
            => _dataService ??= _serviceProvider.GetRequiredService<AuditLogEntityDataService>();

        public AuditLogWriter(
            IServiceProvider serviceProvider,
            ILogger<AuditLogWriter> logger,
            IAuditMaskingPolicy masking,
            IAuditFailureHook failureHook,
            AuditDiagnostics diagnostics,
            IOptions<AuditFailurePolicyOptions> failurePolicy,
            IOptions<AuditEventPublishingOptions> eventPublishing)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _masking = masking ?? throw new ArgumentNullException(nameof(masking));
            _failureHook = failureHook ?? throw new ArgumentNullException(nameof(failureHook));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _failOpen = failurePolicy?.Value.FailOpen ?? true;
            _publishEvents = eventPublishing?.Value.PublishEvents ?? false;
        }

        public async Task SaveAsync(AuditLogEntry entry, CancellationToken ct = default)
        {
            if (entry is null) return;

            try
            {
                var entity = MapToEntity(entry);
                await DataService.EntityCreateAsync(entity, ct);

                if (_publishEvents)
                {
                    await SafePublishAsync(new AuditLogPersisted(
                        entity.Id, entry.ExecutionTime, entry.ServiceName, entry.MethodName,
                        entry.Success, entry.DurationMs, entry.CorrelationId));
                }
            }
            catch (Exception ex)
            {
                _diagnostics.IncrementWriteFailures();
                RaiseFailure(entry, ex);

                _logger.LogWarning(ex, "审计日志写入失败: {ServiceName}.{MethodName}", entry.ServiceName, entry.MethodName);

                if (_publishEvents)
                {
                    await SafePublishAsync(new AuditLogPersistFailed(
                        entry.ServiceName, entry.MethodName, entry.ExecutionTime,
                        ex.GetType().Name, entry.CorrelationId));
                }

                if (!_failOpen)
                {
                    throw;
                }
            }
        }

        private void RaiseFailure(AuditLogEntry entry, Exception ex)
        {
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
            var eventBus = _eventBus ??= _serviceProvider.GetService<ILocalEventBus>();
            if (eventBus is null) return;

            try
            {
                await eventBus.PublishAsync(eventData);
                _diagnostics.IncrementEventsPublished();
            }
            catch (Exception ex)
            {
                _diagnostics.IncrementEventPublishFailures();
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
                ArgumentsJson = _masking.MaskText(entry.ArgumentsJson),
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
