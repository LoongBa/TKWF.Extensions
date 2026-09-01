using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// FreeSql 数据字典存储实现——将 <see cref="DictionaryDefinitionEntity"/> / <see cref="DictionaryItemEntity"/>
    /// 持久化到数据库。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。
    /// 与 FreeSqlSettingStore / FreeSqlBlobRecordStore 模式一致。</para>
    /// </summary>
    internal sealed class FreeSqlDictionaryStore : IDictionaryStore
    {
        private readonly IFreeSql _freeSql;
        private readonly ILogger<FreeSqlDictionaryStore> _logger;

        public FreeSqlDictionaryStore(IFreeSql freeSql, ILogger<FreeSqlDictionaryStore> logger)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DictionaryDefinitionEntity?> GetDefinitionByCodeAsync(string code, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<DictionaryDefinitionEntity>()
                    .Where(d => d.Code == code)
                    .FirstAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典定义按编码读取失败: Code={Code}", code);
                return null;
            }
        }

        public async Task<IReadOnlyList<DictionaryDefinitionEntity>> GetDefinitionsAsync(int skip = 0, int take = 20, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<DictionaryDefinitionEntity>()
                    .OrderByDescending(d => d.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典定义列表读取失败");
                return Array.Empty<DictionaryDefinitionEntity>();
            }
        }

        public async Task<IReadOnlyList<DictionaryItemEntity>> GetItemsAsync(long definitionId, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<DictionaryItemEntity>()
                    .Where(i => i.DefinitionId == definitionId && i.IsEnabled)
                    .OrderBy(i => i.Order)
                    .ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典项列表读取失败: DefinitionId={DefinitionId}", definitionId);
                return Array.Empty<DictionaryItemEntity>();
            }
        }

        public async Task UpsertDefinitionAsync(DictionaryDefinitionEntity definition, CancellationToken ct = default)
        {
            if (definition == null) return;

            try
            {
                var existing = await GetDefinitionByCodeAsync(definition.Code, ct);
                if (existing == null)
                {
                    var newId = await _freeSql.Insert(definition).ExecuteIdentityAsync(ct);
                    definition.Id = newId;
                }
                else
                {
                    definition.Id = existing.Id;
                    definition.CreateTime = existing.CreateTime;
                    definition.UpdateTime = DateTimeOffset.Now;
                    await _freeSql.Update<DictionaryDefinitionEntity>()
                        .SetSource(definition)
                        .ExecuteAffrowsAsync(ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典定义保存失败: Code={Code}", definition.Code);
            }
        }

        public async Task UpsertItemAsync(DictionaryItemEntity item, CancellationToken ct = default)
        {
            if (item == null) return;

            try
            {
                var existing = await _freeSql.Select<DictionaryItemEntity>()
                    .Where(i => i.DefinitionId == item.DefinitionId && i.Code == item.Code)
                    .FirstAsync(ct);

                if (existing == null)
                {
                    var newId = await _freeSql.Insert(item).ExecuteIdentityAsync(ct);
                    item.Id = newId;
                }
                else
                {
                    item.Id = existing.Id;
                    item.CreateTime = existing.CreateTime;
                    item.UpdateTime = DateTimeOffset.Now;
                    await _freeSql.Update<DictionaryItemEntity>()
                        .SetSource(item)
                        .ExecuteAffrowsAsync(ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典项保存失败: DefinitionId={DefinitionId}, Code={Code}", item.DefinitionId, item.Code);
            }
        }

        public async Task DeleteDefinitionAsync(long id, CancellationToken ct = default)
        {
            try
            {
                // 级联清理项
                await _freeSql.Delete<DictionaryItemEntity>()
                    .Where(i => i.DefinitionId == id)
                    .ExecuteAffrowsAsync(ct);

                await _freeSql.Delete<DictionaryDefinitionEntity>()
                    .Where(d => d.Id == id)
                    .ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典定义删除失败: Id={Id}", id);
            }
        }

        public async Task DeleteItemAsync(long id, CancellationToken ct = default)
        {
            try
            {
                await _freeSql.Delete<DictionaryItemEntity>()
                    .Where(i => i.Id == id)
                    .ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典项删除失败: Id={Id}", id);
            }
        }
    }
}