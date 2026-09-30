using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Transactions;

namespace TKWF.Ext.OrganizationUnit
{
    /// <summary>
    /// 组织单元管理实现（public）——树形 OU 生命周期 + 用户关联 + 双向查询。
    /// <para>事务包裹（C1）：Create/Move/Delete 多步写路径经 <see cref="ITransactionManager"/>
    /// BeginAsync → 业务 → CommitAsync / 失败 RollbackAsync（using scope 范式，对齐 Approval CONDITION-1）。</para>
    /// <para>删除语义（C2）：物理删除（hasSoftDelete:false + 实体不声明 IsDeleted），已删 Code 可复用。</para>
    /// <para>Code 白名单 + Path 长度守卫（C3）：拒绝 LIKE 通配符/空白；Path 超 1024 抛业务异常而非 DB Overflow。</para>
    /// <para>数据访问红线：不注入 IFreeSql / IEntityDAC——只经 <see cref="IOrganizationUnitStore"/>（委托 DataService）。</para>
    /// <para>类为 public 但构造函数 internal（<see cref="IOrganizationUnitStore"/> 为 internal 契约）——
    /// 由 <see cref="OrganizationUnitExtensionInitializer{TUserInfo}.ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>
    /// 以工厂方式 TryAddScoped 注册（消费方仍可自定义实现优先）。</para>
    /// </summary>
    public sealed class OrganizationUnitManager : IOrganizationUnitManager
    {
        private readonly IOrganizationUnitStore _store;
        private readonly ITransactionManager _transactionManager;
        private readonly ILogger<OrganizationUnitManager> _logger;

        /// <summary>物化路径最大长度（对齐实体列 MaxLength(1024)）。</summary>
        private const int MaxPathLength = 1024;

        /// <summary>Code 白名单（C3）：字母/数字/下划线/点/连字符。</summary>
        private static readonly Regex CodeRegex = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

        internal OrganizationUnitManager(
            IOrganizationUnitStore store,
            ITransactionManager transactionManager,
            ILogger<OrganizationUnitManager> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<OrganizationUnitEntity> CreateAsync(string code, string name, long? parentId,
            int? sortOrder = null, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (!CodeRegex.IsMatch(code))
                throw new ArgumentException("Code 仅允许字母、数字、下划线、点、连字符（A-Za-z0-9_.-）", nameof(code));
            // P6：拒绝纯点组合（"."/".." 在 Path 段中语义怪异，无正确性影响但展示异常）
            if (code is "." or "..")
                throw new ArgumentException("Code 不允许为纯点组合（. / ..）", nameof(code));

            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                // 父存在性校验 + 路径不变量（事务内，C1）
                int targetLevel;
                string targetPath;
                if (parentId.HasValue)
                {
                    var parent = await _store.GetByIdAsync(parentId.Value, ct);
                    if (parent == null)
                        throw new InvalidOperationException($"父组织单元 {parentId.Value} 不存在");

                    targetLevel = parent.Level + 1;
                    targetPath = parent.Path + code + "/";
                }
                else
                {
                    targetLevel = 0;
                    targetPath = "/" + code + "/";
                }

                // C3：Path 长度守卫——超限抛业务异常而非 DB OverflowError
                if (targetPath.Length > MaxPathLength)
                    throw new InvalidOperationException("组织层级过深或 Code 过长（Path 超过 1024 字符上限）");

                // SortOrder 语义（P1）：默认父下 max+1（同级追加末尾）
                var all = await _store.GetAllAsync(ct);
                int finalSortOrder = sortOrder ?? (all.Where(o => o.ParentId == parentId)
                    .Select(o => (int?)o.SortOrder).Max() ?? -1) + 1;

                var entity = new OrganizationUnitEntity
                {
                    Code = code,
                    Name = name,
                    ParentId = parentId,
                    SortOrder = finalSortOrder,
                    Level = targetLevel,
                    Path = targetPath,
                    IsEnabled = true
                };

                // 并发同 Code 冲突由数据库唯一索引异常自然传播（败者显式异常，对齐 Approval 先例）
                await _store.CreateAsync(entity, ct);

                await scope.CommitAsync(ct);
                return entity;
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task UpdateAsync(long id, string? name = null, int? sortOrder = null,
            bool? isEnabled = null, CancellationToken ct = default)
        {
            var entity = await _store.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"组织单元 {id} 不存在");

            // Code 不可改（F4——更新不触发路径重算）
            if (name != null) entity.Name = name;
            if (sortOrder.HasValue) entity.SortOrder = sortOrder.Value;
            if (isEnabled.HasValue) entity.IsEnabled = isEnabled.Value;
            entity.UpdateTime = DateTimeOffset.Now;

            await _store.UpdateAsync(entity, ct);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(long id, CancellationToken ct = default)
        {
            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                var ou = await _store.GetByIdAsync(id, ct)
                    ?? throw new InvalidOperationException($"组织单元 {id} 不存在");

                // 删除保护（D5/F10）：事务内二次确认——无子节点（全量内存 ParentId 计数）
                var all = await _store.GetAllAsync(ct);
                int childCount = all.Count(o => o.ParentId == id);
                if (childCount > 0)
                    throw new InvalidOperationException($"组织单元含 {childCount} 个子节点，请先移除");

                // 删除保护：无关联用户
                long userCount = await _store.CountUsersByOrganizationUnitIdAsync(id, ct);
                if (userCount > 0)
                    throw new InvalidOperationException($"组织单元含 {userCount} 个关联用户，请先解除");

                // 清 junction → 物理删 OU（原子提交，任何一步失败全回滚）
                await _store.DeleteUsersByOrganizationUnitIdsAsync(new[] { id }, ct);
                await _store.DeleteAsync(id, ct);

                await scope.CommitAsync(ct);
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task MoveAsync(long id, long? newParentId, CancellationToken ct = default)
        {
            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                // 全量 OU 快照（事务内，D7）
                var all = await _store.GetAllAsync(ct);
                var ou = all.FirstOrDefault(o => o.Id == id)
                    ?? throw new InvalidOperationException($"组织单元 {id} 不存在");

                // 循环防护（D9/F9）——ou.Path 已含尾斜杠，StartsWith(ou.Path) 即"自身或后代"前缀
                if (newParentId == id)
                    throw new InvalidOperationException("不能移动到自身");

                OrganizationUnitEntity? newParent = null;
                if (newParentId.HasValue)
                {
                    newParent = all.FirstOrDefault(o => o.Id == newParentId.Value)
                        ?? throw new InvalidOperationException($"父组织单元 {newParentId.Value} 不存在");
                    if (newParent.Path.StartsWith(ou.Path))
                        throw new InvalidOperationException("不能移动到自身或自身后代之下");
                }

                // SortOrder 语义（P1）：移动节点追加到新父末尾（max+1）
                int newSortOrder = (all.Where(o => o.ParentId == newParentId)
                    .Select(o => (int?)o.SortOrder).Max() ?? -1) + 1;

                // BFS 队列重算子树 Level/Path（非递归，防深树栈溢出）
                int rootLevel = newParent == null ? 0 : newParent.Level + 1;
                string rootPath = newParent == null ? $"/{ou.Code}/" : $"{newParent.Path}{ou.Code}/";
                if (rootPath.Length > MaxPathLength)
                    throw new InvalidOperationException("组织层级过深或 Code 过长（Path 超过 1024 字符上限）");

                var queue = new Queue<(long Id, int Level, string Path)>();
                queue.Enqueue((ou.Id, rootLevel, rootPath));

                var updated = new List<OrganizationUnitEntity>();
                while (queue.Count > 0)
                {
                    var (curId, curLevel, curPath) = queue.Dequeue();
                    var node = all.FirstOrDefault(o => o.Id == curId)
                        ?? throw new InvalidOperationException($"组织单元 {curId} 不存在");

                    node.Level = curLevel;
                    node.Path = curPath;
                    // ParentId：仅移动节点本身改挂到新父（null=根）；后代保持原 ParentId（仍是 curId 链）
                    if (curId == id)
                        node.ParentId = newParentId;
                    node.UpdateTime = DateTimeOffset.Now;
                    updated.Add(node);

                    foreach (var child in all.Where(o => o.ParentId == curId))
                    {
                        var childPath = $"{curPath}{child.Code}/";
                        if (childPath.Length > MaxPathLength)
                            throw new InvalidOperationException("组织层级过深或 Code 过长（Path 超过 1024 字符上限）");
                        queue.Enqueue((child.Id, curLevel + 1, childPath));
                    }
                }

                // 移动节点本身 SortOrder 追加到新父末尾
                updated.First(o => o.Id == id).SortOrder = newSortOrder;

                // 全部更新（原子提交——子树 Path/Level 要么全旧要么全新，无半更新）
                foreach (var node in updated)
                    await _store.UpdateAsync(node, ct);

                await scope.CommitAsync(ct);
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task<OrganizationUnitTreeNode> GetTreeAsync(CancellationToken ct = default)
        {
            var all = await _store.GetAllAsync(ct);
            if (all.Count == 0)
                return new OrganizationUnitTreeNode(0, "", "根", 0, true, Array.Empty<OrganizationUnitTreeNode>());

            // 第一遍：Id → 节点映射（Children 占位为空，叶子由后续递归填充）
            var nodeMap = all.ToDictionary(o => o.Id,
                o => new OrganizationUnitTreeNode(
                    o.Id, o.Code, o.Name, o.SortOrder, o.IsEnabled, Array.Empty<OrganizationUnitTreeNode>()));

            // 第二遍：ParentId 归类；孤儿（父不存在）抛异常（评审 P5——不静默归根）
            var roots = new List<OrganizationUnitTreeNode>();
            var childrenMap = new Dictionary<long, List<OrganizationUnitTreeNode>>();

            foreach (var ou in all)
            {
                var node = nodeMap[ou.Id];
                if (!ou.ParentId.HasValue)
                {
                    roots.Add(node);
                }
                else if (!nodeMap.ContainsKey(ou.ParentId.Value))
                {
                    throw new InvalidOperationException(
                        $"组织单元 {ou.Code}(Id={ou.Id}) 的父节点 Id={ou.ParentId.Value} 不存在（数据异常，请检查）");
                }
                else if (!childrenMap.TryGetValue(ou.ParentId.Value, out var siblings))
                {
                    childrenMap[ou.ParentId.Value] = new List<OrganizationUnitTreeNode> { node };
                }
                else
                {
                    siblings.Add(node);
                }
            }

            // 第三遍：递归挂接 Children，根与子级均按 SortOrder 升序。
            // 环检测说明（审核 P4 分析）：ParentId 单父约束 + 根节点（ParentId=null）不出现于任何
            // childrenMap 值——从根出发的父子链唯一确定且数学上不可能成环；Path≤1024 守卫亦间接
            // 限定树深（每段 ≥3 字符 → ≤~340 层），递归栈安全。孤儿防护由第二遍"父不存在"抛异常覆盖。
            roots.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            var assembled = new List<OrganizationUnitTreeNode>(roots.Count);
            foreach (var root in roots)
                assembled.Add(AttachChildren(root, childrenMap));

            return new OrganizationUnitTreeNode(0, "", "根", 0, true, assembled.AsReadOnly());
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<OrganizationUnitEntity>> GetSubTreeAsync(long id, CancellationToken ct = default)
        {
            var ou = await _store.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"组织单元 {id} 不存在");

            // V0.2.0 SQL 下推：ou.Path 已含尾斜杠 → 前缀匹配 = 自身 + 全部子孙（单表精确前缀比较，
            // 非全量 GetAllAsync 内存过滤；LIKE 转义风险见 OrganizationUnitEntityDataService.GetByPathPrefixAsync 注记）
            return await _store.GetByPathPrefixAsync(ou.Path, ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<OrganizationUnitEntity>> GetAncestorsAsync(long id, CancellationToken ct = default)
        {
            var ou = await _store.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"组织单元 {id} 不存在");

            // Path 拆段反查（面包屑）："/A/B/C/" → [A, B, C]；祖先 = 除自身外各段（从根到直接父，不含自身）
            var segments = ou.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length <= 1)
                return Array.Empty<OrganizationUnitEntity>();

            // V0.2.0 SQL 下推：Code IN (A,B,...) 单查询（Code 全局唯一，数据一致时 IN 结果 = 祖先链；
            // 排除自身段（segments 含自身 Code——ancestors = 根→直接父，面包屑语义不含自身，对齐旧实现
            // "for i < segments.Length-1" 逐段 Path==prefix 匹配）；
            // 返回后校验数量——缺失段抛异常，保持 GetAncestors_MissingAncestor_Throws 语义，oracle3 C-6/M3）
            var ancestorCodes = segments.Take(segments.Length - 1).ToList();
            var ancestors = await _store.GetByCodesAsync(ancestorCodes, ct);
            if (ancestors.Count != ancestorCodes.Count)
            {
                var missing = ancestorCodes.Except(ancestors.Select(a => a.Code), StringComparer.Ordinal);
                throw new InvalidOperationException(
                    $"组织单元 {ou.Code} 的祖先 {string.Join("/", missing)} 不存在（数据异常，请检查）");
            }
            return ancestors;
        }

        /// <inheritdoc />
        public async Task AssignUserAsync(long ouId, string userId, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            // 引用守卫（D10）：OU 必须存在
            var ou = await _store.GetByIdAsync(ouId, ct)
                ?? throw new InvalidOperationException($"组织单元 {ouId} 不存在");

            try
            {
                await _store.AddUserAsync(new OrganizationUnitUserEntity
                {
                    OrganizationUnitId = ouId,
                    UserId = userId
                }, ct);
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                // P2：并发重复分配——数据库唯一约束异常转业务异常
                throw new InvalidOperationException("该用户已在此组织单元", ex);
            }
        }

        /// <inheritdoc />
        public async Task UnassignUserAsync(long ouId, string userId, CancellationToken ct = default)
            => await _store.RemoveUserAsync(ouId, userId, ct); // 幂等：不存在静默成功

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetUserIdsInOrganizationUnitAsync(
            long ouId, bool includeDescendants, CancellationToken ct = default)
        {
            // V0.2.0 双模式（oracle3 C-5/M2）：
            //   includeDescendants=true  → 视图单查询（vw_UserOrganizationUnitView JOIN + OUPath 前缀下推，
            //                               替代"子树 Id 集合 → junction"两步；引用守卫 = GetByIdAsync 抛异常，
            //                               语义同旧 GetSubTreeAsync 路径）
            //   includeDescendants=false → 保留 junction 单查（仅本 OU——不存在静默返回空，D18 已删 OU 语义保持）
            if (includeDescendants)
            {
                var ou = await _store.GetByIdAsync(ouId, ct)
                    ?? throw new InvalidOperationException($"组织单元 {ouId} 不存在");
                return await _store.GetUserIdsByOuPathPrefixAsync(ou.Path, ct);
            }

            return await _store.GetUserIdsByOrganizationUnitIdsAsync(new List<long> { ouId }, ct);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<long>> GetOrganizationUnitIdsForUserAsync(string userId, CancellationToken ct = default)
            => _store.GetOrganizationUnitIdsForUserAsync(userId, ct);

        /// <summary>递归把 childrenMap 中的子节点挂到 node 上（同级 SortOrder 升序）。</summary>
        private static OrganizationUnitTreeNode AttachChildren(
            OrganizationUnitTreeNode node,
            IReadOnlyDictionary<long, List<OrganizationUnitTreeNode>> childrenMap)
        {
            if (!childrenMap.TryGetValue(node.Id, out var children) || children.Count == 0)
                return node;

            children.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            var resolved = new List<OrganizationUnitTreeNode>(children.Count);
            foreach (var child in children)
                resolved.Add(AttachChildren(child, childrenMap));

            return node with { Children = resolved.AsReadOnly() };
        }

        /// <summary>
        /// 判定异常链是否含数据库唯一约束冲突。
        /// <para>SQLite：<see cref="Microsoft.Data.Sqlite.SqliteException.SqliteErrorCode"/> 19 = SQLITE_CONSTRAINT；
        /// 兜底消息特征（UNIQUE constraint failed / duplicate key / Duplicate entry）覆盖其他 Provider。</para>
        /// </summary>
        private static bool IsUniqueConstraintViolation(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
                    return true;

                var message = current.Message;
                if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
