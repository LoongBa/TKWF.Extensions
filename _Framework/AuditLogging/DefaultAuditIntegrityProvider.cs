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

            static string F(string? s) => s is null ? "-1:" : s.Length + ":" + s;

            var canonical = string.Concat(
                F(record.Facet.ToString()),
                F(record.Id.ToString()),
                F(record.Actor?.Type.ToString()),
                F(record.Actor?.Id),
                F(record.Target?.TargetType),
                F(record.Target?.TargetId),
                F(record.ActionType),
                F(record.Summary),
                F(record.CorrelationId),
                F(record.ExecutionTime.UtcDateTime.ToString("O")));

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
