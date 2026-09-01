using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典存储抽象——字典定义与字典项的 CRUD 与查询。
    /// <para>由扩展默认 FreeSql 实现（<see cref="FreeSqlDictionaryStore"/>），消费方可自定义（TryAdd 语义）。</para>
    /// </summary>
    public interface IDictionaryStore
    {
        /// <summary>按编码读取字典定义（不存在返回 null）。</summary>
        Task<DictionaryDefinitionEntity?> GetDefinitionByCodeAsync(string code, CancellationToken ct = default);

        /// <summary>分页读取字典定义列表。</summary>
        Task<IReadOnlyList<DictionaryDefinitionEntity>> GetDefinitionsAsync(int skip = 0, int take = 20, CancellationToken ct = default);

        /// <summary>读取指定字典定义的所有项（按 Order 排序，不含已禁用项）。</summary>
        Task<IReadOnlyList<DictionaryItemEntity>> GetItemsAsync(long definitionId, CancellationToken ct = default);

        /// <summary>新增或更新字典定义（按 Code 定位）。</summary>
        Task UpsertDefinitionAsync(DictionaryDefinitionEntity definition, CancellationToken ct = default);

        /// <summary>新增或更新字典项（按 DefinitionId + Code 定位）。</summary>
        Task UpsertItemAsync(DictionaryItemEntity item, CancellationToken ct = default);

        /// <summary>删除字典定义（级联清理其项）。</summary>
        Task DeleteDefinitionAsync(long id, CancellationToken ct = default);

        /// <summary>删除字典项。</summary>
        Task DeleteItemAsync(long id, CancellationToken ct = default);
    }
}