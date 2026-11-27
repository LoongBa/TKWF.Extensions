using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 默认内存水位存储（G8）——消费方可 <c>services.Replace</c> 为持久化实现。
    /// </summary>
    internal sealed class InMemoryAuditWatermarkStore : IAuditWatermarkStore
    {
        private readonly ConcurrentDictionary<string, long> _watermarks = new();

        /// <inheritdoc />
        public Task<long> GetAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_watermarks.TryGetValue(name, out var value) ? value : 0L);

        /// <inheritdoc />
        public Task SetAsync(string name, long value, CancellationToken ct = default)
        {
            _watermarks.AddOrUpdate(name, value, (_, current) => value > current ? value : current);
            return Task.CompletedTask;
        }
    }
}
