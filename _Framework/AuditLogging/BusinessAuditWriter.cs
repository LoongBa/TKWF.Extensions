using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 业务事实审计写入器（<b>接线型</b>，非 <c>IDomainService</c>）。
    /// <para>域帧 <c>User.Use&lt;IBusinessAuditWriter&gt;()</c>；系统帧 <c>provider.GetRequiredService&lt;IBusinessAuditWriter&gt;()</c>。</para>
    /// <para>写失败：诊断计数 + 同步失败回调（恒触发、异常隔离）+ 按 <see cref="AuditFailurePolicyOptions.FailOpen"/> 决定吞/抛。
    /// Actor 必填（entry.Actor 非空）。</para>
    /// </summary>
    internal sealed class BusinessAuditWriter : IBusinessAuditWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BusinessAuditWriter> _logger;
        private readonly IAuditMaskingPolicy _masking;
        private readonly IAuditFailureHook _failureHook;
        private readonly AuditDiagnostics _diagnostics;
        private readonly bool _failOpen;

        private BusinessAuditEntityDataService? _dataService;

        private BusinessAuditEntityDataService DataService
            => _dataService ??= _serviceProvider.GetRequiredService<BusinessAuditEntityDataService>();

        public BusinessAuditWriter(
            IServiceProvider serviceProvider,
            ILogger<BusinessAuditWriter> logger,
            IAuditMaskingPolicy masking,
            IAuditFailureHook failureHook,
            AuditDiagnostics diagnostics,
            IOptions<AuditFailurePolicyOptions> failurePolicy)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _masking = masking ?? throw new ArgumentNullException(nameof(masking));
            _failureHook = failureHook ?? throw new ArgumentNullException(nameof(failureHook));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _failOpen = failurePolicy?.Value.FailOpen ?? true;
        }

        public async Task SaveAsync(BusinessAuditEntry entry, CancellationToken ct = default)
        {
            if (entry is null) return;
            ArgumentNullException.ThrowIfNull(entry.Actor, "审计 Actor 必填（为空会导致审计静默丢失）");
            ArgumentNullException.ThrowIfNull(entry.Target, "审计 Target 必填");
            _ = ParseTenantId(entry.TenantId);

            try
            {
                var entity = Map(entry);
                await DataService.EntityCreateAsync(entity, ct);
            }
            catch (Exception ex)
            {
                _diagnostics.IncrementWriteFailures();
                RaiseFailure(entry, ex);

                _logger.LogWarning(ex,
                    "业务审计写入失败 Target={TargetType}/{TargetId} Action={Action}（facet=Business）",
                    entry.Target.TargetType, entry.Target.TargetId, entry.ActionType);

                if (!_failOpen)
                {
                    throw;
                }
            }
        }

        public async Task SaveBatchAsync(IReadOnlyList<BusinessAuditEntry> entries, CancellationToken ct = default)
        {
            if (entries is null || entries.Count == 0) return;

            foreach (var entry in entries)
            {
                await SaveAsync(entry, ct);
            }
        }

        /// <summary>严格写入——写失败必定抛出（不受 FailOpen 影响；特权删除留痕用）。</summary>
        public async Task SaveStrictAsync(BusinessAuditEntry entry, CancellationToken ct = default)
        {
            if (entry is null) return;
            ArgumentNullException.ThrowIfNull(entry.Actor, "审计 Actor 必填（为空会导致审计静默丢失）");
            ArgumentNullException.ThrowIfNull(entry.Target, "审计 Target 必填");

            try
            {
                var entity = Map(entry);
                await DataService.EntityCreateAsync(entity, ct);
            }
            catch (Exception ex)
            {
                _diagnostics.IncrementWriteFailures();
                RaiseFailure(entry, ex);
                _logger.LogWarning(ex,
                    "业务审计严格写入失败 Target={TargetType}/{TargetId}（facet=Business，strict）",
                    entry.Target.TargetType, entry.Target.TargetId);
                throw;
            }
        }

        private void RaiseFailure(BusinessAuditEntry entry, Exception ex)
        {
            var failure = new AuditPersistFailure(
                AuditFacet.Business,
                DateTimeOffset.UtcNow,
                ServiceName: null,
                MethodName: null,
                entry.CorrelationId,
                ex.GetType().Name);

            try
            {
                _failureHook.OnPersistFailed(failure);
            }
            catch (Exception hookEx)
            {
                _logger.LogWarning(hookEx, "审计失败回调异常（已隔离，不影响审计写入/业务调用）");
            }
        }

        private BusinessAuditEntity Map(BusinessAuditEntry entry)
        {
            return new BusinessAuditEntity
            {
                TenantId = ParseTenantId(entry.TenantId),
                ActorType = entry.Actor.Type.ToString(),
                ActorId = entry.Actor.Id,
                ActorName = _masking.MaskText(entry.Actor.DisplayName),
                TargetType = entry.Target.TargetType,
                TargetId = entry.Target.TargetId,
                ActionType = entry.ActionType,
                Summary = _masking.MaskText(entry.Summary),
                BeforeJson = SerializeMasked(entry.Before),
                AfterJson = SerializeMasked(entry.After),
                AttributesJson = SerializeMasked(entry.Attributes),
                CorrelationId = entry.CorrelationId,
                ExecutionTime = entry.ExecutionTime.UtcDateTime,
                CreateTime = DateTimeOffset.UtcNow,
            };
        }

        private string? SerializeMasked(object? value)
        {
            if (value is null) return null;

            var masked = _masking.Mask(value);
            return masked is null ? null : JsonSerializer.Serialize(masked, JsonOptions);
        }

        private static long? ParseTenantId(string? tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return null;
            if (!long.TryParse(tenantId, out var parsed))
            {
                throw new ArgumentException($"TenantId 必须为数字，收到 '{tenantId}'（fail-closed，防数据丢失/跨租户越权）");
            }
            return parsed;
        }
    }
}
