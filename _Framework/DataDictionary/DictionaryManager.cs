using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典管理实现——组合 <see cref="IDictionaryStore"/> 提供按编码的聚合查询。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class DictionaryManager : IDictionaryManager
    {
        private readonly IDictionaryStore _store;
        private readonly ILogger<DictionaryManager> _logger;

        public DictionaryManager(IDictionaryStore store, ILogger<DictionaryManager> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public Task<DictionaryDefinitionEntity?> GetDefinitionByCodeAsync(string code, CancellationToken ct = default)
            => _store.GetDefinitionByCodeAsync(code, ct);

        /// <inheritdoc />
        public async Task<IReadOnlyList<DictionaryItemEntity>> GetItemsAsync(string code, CancellationToken ct = default)
        {
            var definition = await _store.GetDefinitionByCodeAsync(code, ct);
            if (definition == null)
            {
                _logger.LogWarning("字典项读取失败: 字典定义不存在 Code={Code}", code);
                return Array.Empty<DictionaryItemEntity>();
            }
            return await _store.GetItemsAsync(definition.Id, ct);
        }

        /// <inheritdoc />
        public async Task<DictionaryDefinitionWithItems?> GetDefinitionWithItemsAsync(string code, CancellationToken ct = default)
        {
            var definition = await _store.GetDefinitionByCodeAsync(code, ct);
            if (definition == null) return null;

            var items = await _store.GetItemsAsync(definition.Id, ct);
            return new DictionaryDefinitionWithItems(definition, items);
        }

        /// <inheritdoc />
        public Task UpsertDefinitionAsync(DictionaryDefinitionEntity definition, CancellationToken ct = default)
            => _store.UpsertDefinitionAsync(definition, ct);

        /// <inheritdoc />
        public Task UpsertItemAsync(DictionaryItemEntity item, CancellationToken ct = default)
            => _store.UpsertItemAsync(item, ct);
    }
}