using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 默认完整性提供者——SHA-256 规范化字段拼接（小写十六进制）。
    /// </summary>
    internal sealed class DefaultAuditIntegrityProvider : IAuditIntegrityProvider
    {
        /// <inheritdoc />
        public Task<string> ComputeHashAsync(AuditRecordView record, CancellationToken ct = default)
        {
            if (record is null) throw new ArgumentNullException(nameof(record));

            var canonical = string.Join('|',
                record.Facet.ToString(),
                record.Id.ToString(),
                record.Actor?.Type.ToString(),
                record.Actor?.Id,
                record.Target?.TargetType,
                record.Target?.TargetId,
                record.ActionType,
                record.Summary,
                record.CorrelationId,
                record.ExecutionTime.UtcDateTime.ToString("O"));

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            return Task.FromResult(Convert.ToHexString(hash).ToLowerInvariant());
        }

        /// <inheritdoc />
        public async Task<bool> VerifyAsync(AuditRecordView record, string expectedHash, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(expectedHash)) return false;
            var actual = await ComputeHashAsync(record, ct);
            return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
