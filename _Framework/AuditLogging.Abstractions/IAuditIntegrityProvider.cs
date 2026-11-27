using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计完整性提供者——对单条审计记录计算/校验规范化哈希（可替换，默认 SHA-256）。
    /// <para>用于导出校验、外部归档验真；不改写审计实体（非哈希链落库）。</para>
    /// </summary>
    public interface IAuditIntegrityProvider
    {
        /// <summary>计算记录哈希（小写十六进制）。</summary>
        Task<string> ComputeHashAsync(AuditRecordView record, CancellationToken ct = default);

        /// <summary>校验记录哈希（大小写不敏感比较）。</summary>
        Task<bool> VerifyAsync(AuditRecordView record, string expectedHash, CancellationToken ct = default);
    }
}
