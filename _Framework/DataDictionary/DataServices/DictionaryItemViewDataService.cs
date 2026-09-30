using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.DataDictionary;
using TKWF.Ext.DataDictionary.DTOs;

namespace TKWF.Ext.DataDictionary;

/// <summary>
/// 数据字典项-定义视图只读 DataService（VEntity V0.2.0）——跨表 JOIN 单查询下推 DB。
/// <para>VEntity 由 xCodeGen 跳过 DataService 模板（Engine.cs L42-46），此处手写继承
/// <see cref="DomainReadOnlyDataServiceBase{TEntity, TDto}"/>（2 参数版，与扩展现有 DataService 一致）。
/// 注入 <see cref="IEntityReadOnlyDAC{TEntity}"/>（只读契约）——绝不用 IEntityDAC（数据访问红线）。
/// 不标 <c>[GenerateController(FromDataService = true)]</c>：项查询是 DictionaryManager 聚合内部能力，
/// 数据访问经 IDictionaryManager 门面暴露；ExposeGraphqlQuery=false（C4 树语义保护），无 GraphQL resolver。</para>
/// </summary>
partial class DictionaryItemViewDataService(IDomainUser user, IEntityReadOnlyDAC<DictionaryItemView> dac)
    : DomainReadOnlyDataServiceBase<DictionaryItemView, DictionaryItemViewDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>
    /// 单查询跨表：按 DefinitionCode 返回项列表（JOIN DictionaryItem → DictionaryDefinition 下推 DB，携带定义列，
    /// 替代 GetOrLoadAggregateAsync 项查询两步）。
    /// <para>仅返回启用项（v.IsEnabled——保持"聚合仅含启用项"既有可观测行为，对齐 GetItemsByDefinitionIdAsync），
    /// Order 升序；limit 显式 <c>int.MaxValue</c>（无界——<c>MaxListLimit=1000</c> 保护，项查询语义为全量）。</para>
    /// </summary>
    public async Task<List<DictionaryItemView>> GetByDefinitionCodeAsync(string code, CancellationToken ct = default)
        => await SelectAsync(v => v, predicate: v => v.DefinitionCode == code && v.IsEnabled,
            orderBy: q => q.OrderBy(v => v.Order), limit: int.MaxValue, ct: ct);
}
