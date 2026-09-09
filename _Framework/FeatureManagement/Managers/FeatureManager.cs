using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// 功能管理实现（public sealed，构造函数 internal + 工厂注册）——分层值解析 + 缓存 + 管理写路径。
/// <para>分层（最优先→最不优先）：User → Role（Roles 遍历序首个命中，P4）→ Tenant（仅 TenantId.HasValue）→
/// Global → DefaultValue；匿名（user==null 或 !IsAuthenticated）直查 Global（对齐 Settings 匿名短路）。</para>
/// <para>缓存：IMemoryCache + NotFoundSentinel 负缓存 + BuildCacheKey + 写后 InvalidateCacheForName；
/// Role 层缓存 key 逐角色独立（Feature:Role:{roleName}:{name}）。</para>
/// <para>Global 层唯一性（C4 评审，对齐 FileManagement C2 教训）：ProviderKey=null 可空唯一索引 NULL 互不相同
/// ——SetValueAsync 事务包裹内二次校验（命中更新/未命中创建）。</para>
/// <para>数据访问红线：不注入 IFreeSql/IEntityDAC——只经 <see cref="IFeatureValueStore"/>（委托 DataService）。</para>
/// </summary>
public sealed class FeatureManager : IFeatureManager
{
    private const string NotFoundSentinel = "\x02NOTFOUND\x02";

    private readonly IFeatureValueStore _store;
    private readonly IFeatureDefinitionRepository _definitionRepository;
    private readonly IDomainUser _domainUser;
    private readonly IMemoryCache _cache;
    private readonly FeatureOptions _options;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<FeatureManager> _logger;

    internal FeatureManager(
        IFeatureValueStore store,
        IFeatureDefinitionRepository definitionRepository,
        IDomainUser domainUser,
        IMemoryCache cache,
        IOptions<FeatureOptions> options,
        ITransactionManager transactionManager,
        ILogger<FeatureManager> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
        _domainUser = domainUser ?? throw new ArgumentNullException(nameof(domainUser));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> GetValueAsync(string name, IDomainUser? user,
        string? defaultValue = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var (value, _) = await ResolveLayerAsync(name, user ?? _domainUser, ct);
        // 默认值语义：显式参数优先（调用方指定兜底）→ 未传（null）时定义默认（FeatureDefinition.DefaultValue）
        if (value != null) return value;
        return defaultValue ?? _definitionRepository.GetAll().FirstOrDefault(d => d.Name == name)?.DefaultValue ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(string name, IDomainUser? user, CancellationToken ct = default)
    {
        // fail-closed：空名/未定义/解析失败 → false（框架 IFeatureChecker 契约——不抛异常，防空名致 500）
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var (value, _) = await ResolveLayerAsync(name, user ?? _domainUser, ct);
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

        if (providerName == FeatureProviders.Global)
        {
            // C4：Global 层（ProviderKey=null）唯一性——事务包裹内二次校验（FileManagement C2 完整做法）
            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                var existing = await _store.GetAsync(name, FeatureProviders.Global, null, ct);
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

        InvalidateCacheForName(name);
    }

    /// <inheritdoc />
    public async Task DeleteValueAsync(string name, string providerName, string providerKey,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        string? normalizedKey = providerName == FeatureProviders.Global ? null : providerKey;

        await _store.DeleteAsync(name, providerName, normalizedKey, ct);
        InvalidateCacheForName(name);
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
        => await ResolveLayerAsync(name, user ?? _domainUser, ct);

    // ── 内部：分层解析 ──

    /// <summary>分层解析：User → Role（遍历序）→ Tenant（仅 HasValue）→ Global → (null, "Global")；匿名短路。</summary>
    private async Task<(string? Value, string ProviderName)> ResolveLayerAsync(
        string name, IDomainUser? user, CancellationToken ct)
    {
        if (user == null || !user.IsAuthenticated)
        {
            // 匿名（对齐 Settings）：跳过 User/Role/Tenant 直查 Global
            var global = await GetCachedAsync(name, FeatureProviders.Global, null, ct);
            return (global, FeatureProviders.Global);
        }

        // User 层（最优先）
        var userId = user.UserId;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var userValue = await GetCachedAsync(name, FeatureProviders.User, userId, ct);
            if (userValue != null) return (userValue, FeatureProviders.User);
        }

        // Role 层（Roles 遍历序首个命中——逐角色独立缓存 key，P4）
        var roles = user.UserInfo?.Roles;
        if (roles != null)
        {
            foreach (var role in roles)
            {
                var roleValue = await GetCachedAsync(name, FeatureProviders.Role, role, ct);
                if (roleValue != null) return (roleValue, FeatureProviders.Role);
            }
        }

        // Tenant 层（仅 TenantId.HasValue）
        if (user.TenantId.HasValue)
        {
            var tenantValue = await GetCachedAsync(name, FeatureProviders.Tenant, user.TenantId.Value.ToString(), ct);
            if (tenantValue != null) return (tenantValue, FeatureProviders.Tenant);
        }

        // Global 层
        var globalValue = await GetCachedAsync(name, FeatureProviders.Global, null, ct);
        return (globalValue, FeatureProviders.Global);
    }

    /// <summary>缓存读取：命中返回；未命中查库（负缓存 NotFoundSentinel 防穿透，对齐 Settings）。</summary>
    private async Task<string?> GetCachedAsync(string name, string providerName, string? providerKey, CancellationToken ct)
    {
        var key = BuildCacheKey(name, providerName, providerKey);
        if (_cache.TryGetValue(key, out object? cached))
            return cached is string s && s != NotFoundSentinel ? s : null;

        var entity = await _store.GetAsync(name, providerName, providerKey, ct);
        var ttl = TimeSpan.FromSeconds(Math.Max(1, _options.CacheExpirationSeconds));
        if (entity?.Value == null)
        {
            _cache.Set(key, NotFoundSentinel, ttl);   // 负缓存
            return null;
        }

        _cache.Set(key, entity.Value, ttl);
        return entity.Value;
    }

    private static string BuildCacheKey(string name, string providerName, string? providerKey)
        => $"Feature:{providerName}:{providerKey ?? "Global"}:{name}";

    /// <summary>写后失效：Global 层显式清（最常用层，key 确定）；User/Role/Tenant 层 key 含动态 ProviderKey
    /// （角色名/UserId/TenantId 无法穷举，IMemoryCache 无前缀 API）——依赖 CacheExpirationSeconds TTL 收敛
    /// （v0.1.0 裁定，多实例同理；见使用指南生产注意事项 C5）。</summary>
    private void InvalidateCacheForName(string name)
        => _cache.Remove(BuildCacheKey(name, FeatureProviders.Global, null));

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
