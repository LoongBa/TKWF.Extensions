using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.DataDictionary
{
    /// <summary>
    /// 数据字典管理实现（V0.2.0）——组合 <see cref="IDictionaryStore"/> 提供按编码的聚合查询。
    /// <para>V0.2.0 新增：按 Code 内存缓存（key=<c>DD:{Code}</c>）+ 树形分组组装。</para>
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class DictionaryManager : IDictionaryManager
    {
        private readonly IDictionaryStore _store;
        private readonly ILogger<DictionaryManager> _logger;
        private readonly IMemoryCache _cache;
        private readonly DataDictionaryOptions _options;

        private const string CacheKeyPrefix = "DD:";

        public DictionaryManager(
            IDictionaryStore store,
            ILogger<DictionaryManager> logger,
            IMemoryCache cache,
            IOptions<DataDictionaryOptions> options)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc />
        public async Task<DictionaryDefinitionEntity?> GetDefinitionByCodeAsync(string code, CancellationToken ct = default)
        {
            var aggregate = await GetOrLoadAggregateAsync(code, ct);
            return aggregate?.Definition;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<DictionaryItemEntity>> GetItemsAsync(string code, CancellationToken ct = default)
        {
            var aggregate = await GetOrLoadAggregateAsync(code, ct);
            return aggregate?.Items ?? Array.Empty<DictionaryItemEntity>();
        }

        /// <inheritdoc />
        public async Task<DictionaryDefinitionWithItems?> GetDefinitionWithItemsAsync(string code, CancellationToken ct = default)
            => await GetOrLoadAggregateAsync(code, ct);

        /// <inheritdoc />
        public async Task UpsertDefinitionAsync(DictionaryDefinitionEntity definition, CancellationToken ct = default)
        {
            if (definition == null) return;

            await _store.UpsertDefinitionAsync(definition, ct);
            InvalidateCache(definition.Code);
        }

        /// <inheritdoc />
        public async Task UpsertItemAsync(DictionaryItemEntity item, CancellationToken ct = default)
        {
            if (item == null) return;

            await _store.UpsertItemAsync(item, ct);
            await InvalidateCacheByDefinitionIdAsync(item.DefinitionId, ct);
        }

        /// <inheritdoc />
        public async Task DeleteDefinitionAsync(long id, CancellationToken ct = default)
        {
            // D6：DeleteDefinition 需先按 Id 反查 Code，再删除 + 失效缓存
            await InvalidateCacheByDefinitionIdAsync(id, ct);
            await _store.DeleteDefinitionAsync(id, ct);
        }

        /// <inheritdoc />
        public async Task DeleteItemAsync(long id, CancellationToken ct = default)
        {
            // D6：DeleteItem 需先按 Id 查 DefinitionId → 再查 Code → 失效缓存
            await InvalidateCacheByItemIdAsync(id, ct);
            await _store.DeleteItemAsync(id, ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<DictionaryTreeNode>> GetItemsTreeAsync(string code, CancellationToken ct = default)
        {
            if (!_options.EnableTreeMode)
            {
                // D7：EnableTreeMode=false 降级行为——返回平铺列表，每项 Children 为空列表
                var flatItems = await GetItemsAsync(code, ct);
                return flatItems
                    .Select(i => new DictionaryTreeNode(
                        i.Code,
                        i.DisplayName,
                        i.Value,
                        i.Order,
                        i.IsEnabled,
                        Array.Empty<DictionaryTreeNode>()))
                    .ToList();
            }

            var aggregate = await GetOrLoadAggregateAsync(code, ct);
            if (aggregate == null)
            {
                _logger.LogWarning("树形读取失败: 字典定义不存在 Code={Code}", code);
                return Array.Empty<DictionaryTreeNode>();
            }

            return BuildTree(aggregate.Items);
        }

        /// <summary>
        /// 从平铺列表递归组装树形结构。
        /// <para>根节点 = ParentCode 为 null/空 或 父节点不存在的项（归根）；子节点按 ParentCode 匹配挂接。</para>
        /// </summary>
        private static IReadOnlyList<DictionaryTreeNode> BuildTree(IReadOnlyList<DictionaryItemEntity> items)
        {
            if (items.Count == 0)
                return Array.Empty<DictionaryTreeNode>();

            // 第一遍：建立 Code → 节点映射（Children 占位为空，叶子由后续递归填充）
            var nodeMap = items.ToDictionary(
                i => i.Code,
                i => new DictionaryTreeNode(
                    i.Code,
                    i.DisplayName,
                    i.Value,
                    i.Order,
                    i.IsEnabled,
                    Array.Empty<DictionaryTreeNode>()));

            // 第二遍：按 ParentCode 归类；父缺失/父不存在 → 归根
            var roots = new List<DictionaryTreeNode>();
            var childrenMap = new Dictionary<string, List<DictionaryTreeNode>>();

            foreach (var item in items)
            {
                var node = nodeMap[item.Code];
                if (string.IsNullOrEmpty(item.ParentCode) || !nodeMap.ContainsKey(item.ParentCode!))
                {
                    roots.Add(node);
                }
                else if (!childrenMap.TryGetValue(item.ParentCode!, out var siblings))
                {
                    childrenMap[item.ParentCode!] = new List<DictionaryTreeNode> { node };
                }
                else
                {
                    siblings.Add(node);
                }
            }

            // 第三遍：递归挂接 Children，根与子级均按 Order 升序
            roots.Sort((a, b) => a.Order.CompareTo(b.Order));
            var assembled = new List<DictionaryTreeNode>(roots.Count);
            foreach (var root in roots)
                assembled.Add(AttachChildren(root, childrenMap));

            return assembled.AsReadOnly();
        }

        /// <summary>递归把 childrenMap 中的子节点挂到 node 上（按 Order 升序）。</summary>
        private static DictionaryTreeNode AttachChildren(
            DictionaryTreeNode node,
            IReadOnlyDictionary<string, List<DictionaryTreeNode>> childrenMap)
        {
            if (!childrenMap.TryGetValue(node.Code, out var children) || children.Count == 0)
                return node;

            children.Sort((a, b) => a.Order.CompareTo(b.Order));
            var resolved = new List<DictionaryTreeNode>(children.Count);
            foreach (var child in children)
                resolved.Add(AttachChildren(child, childrenMap));

            return node with { Children = resolved.AsReadOnly() };
        }

        /// <summary>
        /// 从缓存或 Store 加载聚合（定义 + 项）。
        /// <para>缓存 key = <c>DD:{Code}</c>，存储 <see cref="DictionaryDefinitionWithItems"/>。</para>
        /// </summary>
        private async Task<DictionaryDefinitionWithItems?> GetOrLoadAggregateAsync(string code, CancellationToken ct)
        {
            var cacheKey = $"{CacheKeyPrefix}{code}";

            if (_options.EnableCache && _cache.TryGetValue(cacheKey, out DictionaryDefinitionWithItems? cached) && cached != null)
                return cached;

            var definition = await _store.GetDefinitionByCodeAsync(code, ct);
            if (definition == null) return null;

            var items = await _store.GetItemsAsync(definition.Id, ct);
            var aggregate = new DictionaryDefinitionWithItems(definition, items);

            if (_options.EnableCache)
            {
                var expiration = TimeSpan.FromSeconds(_options.CacheExpirationSeconds);
                _cache.Set(cacheKey, aggregate, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiration
                });
            }

            return aggregate;
        }

        /// <summary>按 Code 失效缓存（写入后调用）。</summary>
        private void InvalidateCache(string code)
        {
            if (!_options.EnableCache) return;
            if (string.IsNullOrEmpty(code)) return;
            _cache.Remove($"{CacheKeyPrefix}{code}");
        }

        /// <summary>
        /// 按 DefinitionId 反查 Code 后失效缓存（UpsertItem/DeleteDefinition 场景，D6）。
        /// <para>一次查询（按 Id 查 Definition → Code）可接受，失败时缓存过期兜底。</para>
        /// </summary>
        private async Task InvalidateCacheByDefinitionIdAsync(long definitionId, CancellationToken ct)
        {
            if (!_options.EnableCache) return;

            try
            {
                var definition = await _store.GetDefinitionByIdAsync(definitionId, ct);
                if (definition != null)
                    InvalidateCache(definition.Code);
            }
            catch (Exception ex)
            {
                // D6：失败靠过期兜底（300s 默认）
                _logger.LogWarning(ex, "缓存失效失败（DefinitionId={DefinitionId}），依赖过期兜底", definitionId);
            }
        }

        /// <summary>
        /// 按 ItemId 反查 DefinitionId → 再查 Code 后失效缓存（DeleteItem 场景，D6）。
        /// <para>两次查询（先按 Id 查 Item 得 DefinitionId，再按 Id 查 Definition 得 Code），失败时缓存过期兜底。</para>
        /// </summary>
        private async Task InvalidateCacheByItemIdAsync(long itemId, CancellationToken ct)
        {
            if (!_options.EnableCache) return;

            try
            {
                var item = await _store.GetItemByIdAsync(itemId, ct);
                if (item != null)
                    await InvalidateCacheByDefinitionIdAsync(item.DefinitionId, ct);
            }
            catch (Exception ex)
            {
                // D6：失败靠过期兜底
                _logger.LogWarning(ex, "缓存失效失败（ItemId={ItemId}），依赖过期兜底", itemId);
            }
        }
    }
}
