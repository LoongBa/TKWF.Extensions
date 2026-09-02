using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>字典定义 + 其项集合的聚合返回。</summary>
    /// <param name="Definition">字典定义。</param>
    /// <param name="Items">字典项（按 Order 排序，不含禁用）。</param>
    public record DictionaryDefinitionWithItems(
        DictionaryDefinitionEntity Definition,
        IReadOnlyList<DictionaryItemEntity> Items);

    /// <summary>
    /// 数据字典管理门面——按编码读取定义/项/完整集合，屏蔽 Store 细节。
    /// <para>由扩展默认实现（<see cref="DictionaryManager"/>），消费方可自定义（TryAdd 语义）。</para>
    /// </summary>
    public interface IDictionaryManager
    {
        /// <summary>按编码读取字典定义（不存在返回 null）。</summary>
        Task<DictionaryDefinitionEntity?> GetDefinitionByCodeAsync(string code, CancellationToken ct = default);

        /// <summary>按编码读取字典项（不存在返回空列表）。</summary>
        Task<IReadOnlyList<DictionaryItemEntity>> GetItemsAsync(string code, CancellationToken ct = default);

        /// <summary>按编码读取完整字典（定义 + 项集合；定义不存在返回 null）。</summary>
        Task<DictionaryDefinitionWithItems?> GetDefinitionWithItemsAsync(string code, CancellationToken ct = default);

        /// <summary>新增或更新字典定义（按 Code 定位，幂等）。</summary>
        Task UpsertDefinitionAsync(DictionaryDefinitionEntity definition, CancellationToken ct = default);

        /// <summary>新增或更新字典项（按 DefinitionId + Code 定位，幂等）。</summary>
        Task UpsertItemAsync(DictionaryItemEntity item, CancellationToken ct = default);

        /// <summary>删除字典定义（级联清理其项；V0.2.0 删除后按 Code 失效缓存）。</summary>
        Task DeleteDefinitionAsync(long id, CancellationToken ct = default);

        /// <summary>删除字典项（V0.2.0 删除后反查所属定义并按 Code 失效缓存）。</summary>
        Task DeleteItemAsync(long id, CancellationToken ct = default);

        /// <summary>
        /// 按编码读取字典项并组装为树形结构（V0.2.0）。
        /// <para>需 <see cref="DataDictionaryOptions.EnableTreeMode"/> = true；
        /// 当 EnableTreeMode = false 时降级为返回平铺列表（每项 Children 为空列表）。</para>
        /// </summary>
        /// <param name="code">字典编码。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>树形节点列表（根节点为 ParentCode = null 的项）；字典不存在返回空列表。</returns>
        Task<IReadOnlyList<DictionaryTreeNode>> GetItemsTreeAsync(string code, CancellationToken ct = default);
    }
}