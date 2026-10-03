using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典存储实现——经 <see cref="DictionaryDefinitionEntityDataService"/> + <see cref="DictionaryItemEntityDataService"/>
    /// （SG1/xCodeGen 生成的 DataService）委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：
    /// 扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
internal sealed class DictionaryStore : IDictionaryStore
    {
        private readonly IDomainUser _user;
        private DictionaryDefinitionEntityDataService? _definitionDataService;
        private DictionaryItemEntityDataService? _itemDataService;
        private DictionaryItemViewDataService? _itemViewDataService;
        private readonly ILogger<DictionaryStore> _logger;

        private DictionaryDefinitionEntityDataService DefinitionDataService => _definitionDataService ??= _user.Use<DictionaryDefinitionEntityDataService>();
        private DictionaryItemEntityDataService ItemDataService => _itemDataService ??= _user.Use<DictionaryItemEntityDataService>();
        private DictionaryItemViewDataService ItemViewDataService => _itemViewDataService ??= _user.Use<DictionaryItemViewDataService>();

        public DictionaryStore(
            IDomainUser user,
            ILogger<DictionaryStore> logger)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DictionaryDefinitionEntity?> GetDefinitionByCodeAsync(string code, CancellationToken ct = default)
        {
            try
            {
                return await DefinitionDataService.GetByCodeAsync(code, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典定义按编码读取失败: Code={Code}", code);
                return null;
            }
        }

        public async Task<DictionaryDefinitionEntity?> GetDefinitionByIdAsync(long id, CancellationToken ct = default)
        {
            try
            {
                return await DefinitionDataService.GetEntityByIdAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典定义按 Id 读取失败: Id={Id}", id);
                return null;
            }
        }

        public async Task<DictionaryItemEntity?> GetItemByIdAsync(long id, CancellationToken ct = default)
        {
            try
            {
                return await ItemDataService.GetEntityByIdAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典项按 Id 读取失败: Id={Id}", id);
                return null;
            }
        }

        public async Task<IReadOnlyList<DictionaryDefinitionEntity>> GetDefinitionsAsync(int skip = 0, int take = 20, CancellationToken ct = default)
        {
            try
            {
                return await DefinitionDataService.GetDefinitionsPagedAsync(skip, take, ct);
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
                return await ItemDataService.GetItemsByDefinitionIdAsync(definitionId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典项列表读取失败: DefinitionId={DefinitionId}", definitionId);
return Array.Empty<DictionaryItemEntity>();
            }
        }

        public async Task<List<DictionaryItemView>> GetItemsByDefinitionCodeAsync(string code, CancellationToken ct = default)
        {
            try
            {
                return await ItemViewDataService.GetByDefinitionCodeAsync(code, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典项视图按编码读取失败: Code={Code}", code);
                return new List<DictionaryItemView>();
            }
        }

        public async Task UpsertDefinitionAsync(DictionaryDefinitionEntity definition, CancellationToken ct = default)
        {
            if (definition == null) return;

            try
            {
                await DefinitionDataService.UpsertByCodeAsync(definition, ct);
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
                await ItemDataService.UpsertByKeyAsync(item, ct);
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
                // 级联：先删项，再删定义
                await ItemDataService.DeleteItemsByDefinitionIdAsync(id, ct);
                await DefinitionDataService.DeleteEntityAsync(id, ct);
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
                await ItemDataService.DeleteEntityAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "字典项删除失败: Id={Id}", id);
            }
        }
    }
}
