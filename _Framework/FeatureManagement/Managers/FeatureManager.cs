using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Events;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// 功能管理实现（public sealed，构造函数 internal + 工厂注册）——Provider 链解析 + 版本号缓存 + 管理写路径 + 变更事件。
/// <para>v0.2.0：分层解析 Provider 化（<see cref="IFeatureValueProvider"/> 链 + <see cref="FeatureOptions.ProviderOrder"/> 顺序），
/// 内置四层（User→Role→Tenant→Global）行为与 v0.1.0 完全一致；缓存改<b>版本号 key</b>
/// （<see cref="FeatureCacheVersionRegistry"/>——写后 version++ 全层天然失效，修复 v0.1.0 User/Role/Tenant 层 TTL 收敛缺陷）；
/// 写路径发布 <see cref="FeatureValueChangedEvent"/>（Commit 后，消费方钩子——审计/跨实例联动）。</para>
/// <para>用户契约（C2）：接收 <see cref="IDomainUser"/>（租户/认证/角色成员在其上）；匿名（user==null 或 !IsAuthenticated）直查 Global。</para>
/// <para>Global 唯一性（C4）：ProviderKey=null 可空唯一索引 NULL 互不相同——SetValueAsync 事务包裹 + 事务内二次校验。</para>
/// <para>数据访问红线：不注入 IFreeSql/IEntityDAC——只经 <see cref="IFeatureValueStore"/>（委托 DataService）。</para>
/// </summary>
public sealed class FeatureManager : IFeatureManager
{
    private const string NotFoundSentinel = "\x02NOTFOUND\x02";

    private readonly IFeatureValueStore _store;
    private readonly IFeatureDefinitionRepository _definitionRepository;
    private readonly IReadOnlyList<IFeatureValueProvider> _providers;
    private readonly FeatureCacheVersionRegistry _versionRegistry;
    private readonly IDomainUser _domainUser;
    private readonly IMemoryCache _cache;
    private readonly FeatureOptions _options;
    private readonly ITransactionManager _transactionManager;
    private readonly ILocalEventBus _eventBus;
    private readonly ILogger<FeatureManager> _logger;

    // Provider 链缓存（Name 冲突懒校验结果 + 排序后顺序——首次解析构建，防重复检测，C4）
    private IReadOnlyList<IFeatureValueProvider>? _orderedProvidersCache;

    internal FeatureManager(
        IFeatureValueStore store,
        IFeatureDefinitionRepository definitionRepository,
        IEnumerable<IFeatureValueProvider> providers,
        FeatureCacheVersionRegistry versionRegistry,
        IDomainUser domainUser,
        IMemoryCache cache,
        IOptions<FeatureOptions> options,
        ITransactionManager transactionManager,
        ILocalEventBus eventBus,
        ILogger<FeatureManager> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        _versionRegistry = versionRegistry ?? throw new ArgumentNullException(nameof(versionRegistry));
        _domainUser = domainUser ?? throw new ArgumentNullException(nameof(domainUser));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> GetValueAsync(string name, IDomainUser? user,
        string? defaultValue = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var (value, _) = await ResolveViaProvidersAsync(name, user ?? _domainUser, ct);
        if (value != null) return value;

        // 默认值语义：显式参数优先 → 定义默认（FeatureDefinition.DefaultValue）
        return defaultValue ?? _definitionRepository.GetAll().FirstOrDefault(d => d.Name == name)?.DefaultValue ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(string name, IDomainUser? user, CancellationToken ct = default)
    {
        // fail-closed：空名/未定义/解析失败 → false（框架 IFeatureChecker 契约——不抛异常，防空名致 500）
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var (value, _) = await ResolveViaProvidersAsync(name, user ?? _domainUser, ct);
        if (value == null)
            return false;   // fail-closed：未定义/无值

        // P3：命中层值存在但 bool 解析失败 → false（不跨层回退——命中层值优先，解析失败即关闭）
        return bool.TryParse(value, out var enabled) && enabled;
    }

    /// <inheritdoc />
    public async Task SetValueAsync(string name, string value, string providerName, string providerKey,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        if (providerName != FeatureProviders.Global && string.IsNullOrWhiteSpace(providerKey))
            throw new ArgumentException($"Provider 层 {providerName} 必须提供 ProviderKey", nameof(providerKey));

        string? oldValue;
        if (providerName == FeatureProviders.Global)
        {
            // C4：Global 层（ProviderKey=null）唯一性——事务包裹内二次校验（FileManagement C2 完整做法）
            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                var existing = await _store.GetAsync(name, FeatureProviders.Global, null, ct);
                oldValue = existing?.Value;
                if (existing != null)
                {
                    existing.Value = value;
                    existing.UpdateTime = DateTime.UtcNow;
                    await _store.SetAsync(existing, ct);
                }
                else
                {
                    await _store.SetAsync(new FeatureValueEntity
                    {
                        Name = name,
                        Value = value,
                        ProviderName = FeatureProviders.Global,
                        ProviderKey = null,
                        UpdateTime = DateTime.UtcNow
                    }, ct);
                }

                await scope.CommitAsync(ct);
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }
        else
        {
            // 非 Global：UX_FeatureValue_Name_Provider 数据库唯一约束兜底（冲突转业务异常）
            // OldValue 记录（P4：额外一次读——事件通知性语义，并发写为读取时快照）
            var existing = await _store.GetAsync(name, providerName, providerKey, ct);
            oldValue = existing?.Value;
            try
            {
                await _store.SetAsync(new FeatureValueEntity
                {
                    Name = name,
                    Value = value,
                    ProviderName = providerName,
                    ProviderKey = providerKey,
                    UpdateTime = DateTime.UtcNow
                }, ct);
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                throw new InvalidOperationException($"Feature 值已存在：{name}@{providerName}:{providerKey}", ex);
            }
        }

        // 写成功：version++ 全层失效（即时，无需事件）+ 发布变更事件（Commit 后——消费方钩子）
        InvalidateCacheForName(name);
        await PublishChangedAsync(name, providerName, providerKey, oldValue, value, ct);
    }

    /// <inheritdoc />
    public async Task DeleteValueAsync(string name, string providerName, string providerKey,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        string? normalizedKey = providerName == FeatureProviders.Global ? null : providerKey;

        // OldValue 记录（P4）
        var existing = await _store.GetAsync(name, providerName, normalizedKey, ct);
        string? oldValue = existing?.Value;

        await _store.DeleteAsync(name, providerName, normalizedKey, ct);

        InvalidateCacheForName(name);
        await PublishChangedAsync(name, providerName, normalizedKey, oldValue, null, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FeatureDefinition>> GetDefinitionsAsync(CancellationToken ct = default)
        => Task.FromResult(_definitionRepository.GetAll());

    /// <inheritdoc />
    public Task<IReadOnlyList<FeatureValueEntity>> GetFeatureValuesAsync(
        string? providerName = null, string? providerKey = null, CancellationToken ct = default)
        => _store.GetListAsync(providerName, providerKey, ct);   // 读路径静默降级（Store 语义）

    /// <inheritdoc />
    public async Task<(string? Value, string ProviderName)> GetEffectiveValueAsync(
        string name, IDomainUser? user, CancellationToken ct = default)
        => await ResolveViaProvidersAsync(name, user ?? _domainUser, ct);

    // ── 内部：Provider 链解析 ──

    /// <summary>Provider 链解析（v0.2.0）：按 ProviderOrder 排序遍历，首个命中返回；匿名短路直查 Global。</summary>
    private async Task<(string? Value, string ProviderName)> ResolveViaProvidersAsync(
        string name, IDomainUser? user, CancellationToken ct)
    {
        var ordered = GetOrderedProviders();
        bool isAnonymous = user == null || !user.IsAuthenticated;
        var definition = _definitionRepository.GetAll().FirstOrDefault(d => d.Name == name);
        var allowed = definition?.AllowedProviders;

        foreach (var provider in ordered)
        {
            // 匿名短路：仅 Global（跳过其余 Provider）
            if (isAnonymous && provider.Name != FeatureProviders.Global)
                continue;

            // AllowedProviders 过滤（定义级——非空且不含该 Provider → 跳过）
            if (allowed != null && !allowed.Contains(provider.Name))
                continue;

            // providerKey 解析（内置四层：User=UserId / Role=角色遍历序逐个 / Tenant=TenantId / Global=null；自定义=null）
            if (provider.Name == FeatureProviders.User)
            {
                var userId = user!.UserId;
                if (string.IsNullOrWhiteSpace(userId)) continue;
                var userValue = await GetCachedAsync(name, provider, userId, ct);
                if (userValue != null) return (userValue, provider.Name);
            }
            else if (provider.Name == FeatureProviders.Role)
            {
                var roles = user!.UserInfo?.Roles;
                if (roles == null) continue;
                foreach (var role in roles)
                {
                    var roleValue = await GetCachedAsync(name, provider, role, ct);
                    if (roleValue != null) return (roleValue, provider.Name);
                }
            }
            else if (provider.Name == FeatureProviders.Tenant)
            {
                if (!user!.TenantId.HasValue) continue;
                var tenantValue = await GetCachedAsync(name, provider, user.TenantId.Value.ToString(), ct);
                if (tenantValue != null) return (tenantValue, provider.Name);
            }
            else if (provider.Name == FeatureProviders.Global)
            {
                var globalValue = await GetCachedAsync(name, provider, null, ct);
                if (globalValue != null) return (globalValue, provider.Name);
            }
            else
            {
                // 自定义 Provider：providerKey 由 Provider 注入上下文自行处理（接口统一传 null）
                var customValue = await GetCachedAsync(name, provider, null, ct);
                if (customValue != null) return (customValue, provider.Name);
            }
        }

        return (null, FeatureProviders.Global);
    }

    /// <summary>Provider 链排序 + Name 冲突懒校验（C4：首次解析构建缓存，防重复检测）。</summary>
    private IReadOnlyList<IFeatureValueProvider> GetOrderedProviders()
    {
        if (_orderedProvidersCache != null) return _orderedProvidersCache;

        // Name 冲突校验（注册集合内重复）
        var duplicates = _providers.GroupBy(p => p.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException($"Feature Provider Name 冲突：{string.Join(", ", duplicates)}");

        // ProviderOrder 排序：列入的按配置顺序；未列入的追加末尾（注册顺序）；空列表 = 注册顺序
        var order = _options.ProviderOrder ?? [];
        var orderedList = order.Count > 0
            ? order.Select(o => _providers.FirstOrDefault(p => p.Name == o)).Where(p => p != null).Cast<IFeatureValueProvider>()
                  .Concat(_providers.Where(p => !order.Contains(p.Name))).ToList()
            : _providers.ToList();

        _orderedProvidersCache = orderedList;
        return orderedList;
    }

    /// <summary>缓存读取（版本号 key）：命中返回；未命中查库（负缓存 NotFoundSentinel 防穿透，对齐 Settings）。</summary>
    private async Task<string?> GetCachedAsync(string name, IFeatureValueProvider provider, string? providerKey, CancellationToken ct)
    {
        var key = BuildCacheKey(name, provider.Name, providerKey);
        if (_cache.TryGetValue(key, out object? cached))
            return cached is string s && s != NotFoundSentinel ? s : null;

        var entity = await provider.GetOrNullAsync(name, providerKey ?? "", ct);
        var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.CacheExpirationSeconds));
        if (entity == null)
        {
            _cache.Set(key, NotFoundSentinel, ttl);   // 负缓存
            return null;
        }

        _cache.Set(key, entity, ttl);
        return entity;
    }

    /// <summary>缓存 key：版本号嵌入（v0.2.0）——写后 version++ → 全层新 key miss（无需登记枚举动态 ProviderKey）。</summary>
    private string BuildCacheKey(string name, string providerName, string? providerKey)
        => $"Feature:{name}:v{_versionRegistry.GetVersion(name)}:{providerName}:{providerKey ?? "Global"}";

    /// <summary>写后失效：版本递增（O(1)——全层天然失效；旧 key 由 TTL 清理，无泄漏）。</summary>
    private void InvalidateCacheForName(string name)
        => _versionRegistry.BumpVersion(name);

    /// <summary>发布变更事件（Commit 后——AOP Bag commit 后派发 / 非 AOP 立即；消费方钩子，不用于本进程失效）。</summary>
    private Task PublishChangedAsync(string name, string providerName, string? providerKey,
        string? oldValue, string? newValue, CancellationToken ct)
        => _eventBus.PublishAsync(new FeatureValueChangedEvent(name, providerName, providerKey, oldValue, newValue));

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
                return true;
            var message = current.Message;
            if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)) return true;
            if (message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
            if (message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
