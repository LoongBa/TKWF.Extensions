using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计水位存储（G8）——记录各面/各消费方已处理的 Id 水位，供 Outbox/增量消费去重。
    /// <para>默认内存实现；生产建议替换为持久化实现（同库表或分布式缓存）。</para>
    /// </summary>
    public interface IAuditWatermarkStore
    {
        /// <summary>读取水位（不存在返回 0）。</summary>
        Task<long> GetAsync(string name, CancellationToken ct = default);

        /// <summary>写入水位（应单调递增，调用方保证）。</summary>
        Task SetAsync(string name, long value, CancellationToken ct = default);
    }
}
