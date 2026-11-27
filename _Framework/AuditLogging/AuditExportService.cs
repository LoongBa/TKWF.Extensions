using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计导出实现（G6）——门面帧内枚举 Reader，按 keyset 分页流式写出 CSV/JSONL。
    /// <para>缺面 → <see cref="AuditFacetUnavailableException"/>（C1 一致语义）。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class AuditExportService : DomainServiceBase, IAuditExportService
    {
        private const int PageSize = 200;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly ILogger<AuditExportService> _logger;
        private readonly long _maxRows;
        private readonly IReadOnlyList<IAuditFacetReader> _readers;

        public AuditExportService(
            IDomainUser user, ILogger<AuditExportService> logger, IOptions<AuditExportOptions> options,
            IEnumerable<IAuditFacetReader> readers) : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxRows = options?.Value.MaxRows ?? 10_000;
            _readers = (readers ?? Enumerable.Empty<IAuditFacetReader>()).ToList();
        }

        private IReadOnlyList<IAuditFacetReader> Readers => _readers;

        /// <inheritdoc />
        public async Task<long> ExportAsync(
            AuditFacet facet, AuditFacetQuery query, AuditExportFormat format, Stream output, CancellationToken ct = default)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));
            if (output is null) throw new ArgumentNullException(nameof(output));

            var policy = User.GetService<IAuditAccessPolicy>()
                         ?? throw new AuditAccessDeniedException("审计访问策略未注册（fail-closed）");
            var decision = await policy.EvaluateAsync(
                new AuditAccessRequest(AuditOperation.Export, User.UserId, User.TenantId?.ToString(), User.IsAuthenticated, User.IsSystemActor), ct);
            if (!decision.Allowed)
                throw new AuditAccessDeniedException(decision.Reason);

            var reader = Readers.FirstOrDefault(r => r.Facet == facet)
                         ?? throw new AuditFacetUnavailableException(facet);

            var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, leaveOpen: true);
            try
            {
                if (format == AuditExportFormat.Csv)
                {
                    await writer.WriteLineAsync("Facet,Id,ActorType,ActorId,ActorName,TargetType,TargetId,ActionType,Summary,CorrelationId,ExecutionTime");
                }

                long total = 0;
                long? cursor = query.AfterId;
                while (true)
                {
                    var page = await reader.QueryAsync(
                        query with { AfterId = cursor, Skip = 0, Take = PageSize }, ct);
                    if (page.Items.Count == 0) break;

                    foreach (var item in page.Items)
                    {
                        if (total >= _maxRows)
                            throw new AuditExportLimitExceededException(facet, _maxRows);
                        await WriteRowAsync(writer, item, format);
                        total++;
                    }

                    cursor = page.Items[^1].Id;
                    if (page.Items.Count < PageSize) break;
                }

                await writer.FlushAsync(ct);
                _logger.LogInformation("审计导出 {Facet} {Format} 共 {Count} 条", facet, format, total);
                return total;
            }
            finally
            {
                await writer.DisposeAsync();
            }
        }

        private static async Task WriteRowAsync(StreamWriter writer, AuditRecordView item, AuditExportFormat format)
        {
            var actorType = item.Actor?.Type.ToString();
            var actorId = item.Actor?.Id;
            var actorName = item.Actor?.DisplayName;
            var targetType = item.Target?.TargetType;
            var targetId = item.Target?.TargetId;
            var executionTime = item.ExecutionTime.UtcDateTime.ToString("O");

            if (format == AuditExportFormat.Csv)
            {
                var line = string.Join(',',
                    Csv(item.Facet.ToString()),
                    item.Id.ToString(),
                    Csv(actorType),
                    Csv(actorId),
                    Csv(actorName),
                    Csv(targetType),
                    Csv(targetId),
                    Csv(item.ActionType),
                    Csv(item.Summary),
                    Csv(item.CorrelationId),
                    Csv(executionTime));
                await writer.WriteLineAsync(line);
                return;
            }

            var row = new ExportRow(
                item.Facet.ToString(), item.Id, actorType, actorId, actorName,
                targetType, targetId, item.ActionType, item.Summary, item.CorrelationId, executionTime);
            await writer.WriteLineAsync(JsonSerializer.Serialize(row, JsonOptions));
        }

        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
                value = "'" + value;
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private sealed record ExportRow(
            string Facet,
            long Id,
            string? ActorType,
            string? ActorId,
            string? ActorName,
            string? TargetType,
            string? TargetId,
            string? ActionType,
            string? Summary,
            string? CorrelationId,
            string ExecutionTime);
    }
}
