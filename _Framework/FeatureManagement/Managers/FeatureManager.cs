using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
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
/// <para>v0.3.0：类型化读写（<see cref="GetValueAsync{T}(string, IDomainUser?, T, CancellationToken)"/> /
/// <see cref="SetValueAsync{T}(string, T, string, string, CancellationToken)"/>）——序列化/反序列化映射集中于本类
/// 私有静态方法（bool/数字/DateTime 规范字符串 + 其他类型 JSON）；写时校验 <see cref="ValidateValueForDefinition"/>
/// （对齐定义 ValueType，违反 → <see cref="ArgumentException"/>；未定义 Feature → 跳过校验向后兼容）。</para>
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

        // v0.3.0 写时校验（对齐定义 ValueType——写错类型立即暴露，不再延迟到读时；未定义 Feature → 跳过校验向后兼容）
        ValidateValueForDefinition(name, value);

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

    /// <inheritdoc />
    public async Task<T> GetValueAsync<T>(string name, IDomainUser? user, T defaultValue = default!, CancellationToken ct = default)
    {
        var (value, _) = await ResolveViaProvidersAsync(name, user ?? _domainUser, ct);
        if (value != null)
            return DeserializeValue(value, defaultValue, _logger);

        // 无存储值：定义 DefaultValue 优先（P6：与字符串入口回退链相反——字符串入口为参数优先 defaultValue ?? definition.DefaultValue；
        // 类型化入口因签名 default! 区分不了 default(T) 与显式默认，故让定义默认（Feature 设计者声明的规范回退值）优先）；
        // 不可解析/无定义默认 → defaultValue
        var definition = _definitionRepository.GetAll().FirstOrDefault(d => d.Name == name);
        if (definition?.DefaultValue is { Length: > 0 } definitionDefault)
            return DeserializeValue(definitionDefault, defaultValue, _logger);

        return defaultValue;
    }

    /// <inheritdoc />
    public async Task SetValueAsync<T>(string name, T value, string providerName, string providerKey,
        CancellationToken ct = default)
        => await SetValueAsync(name, SerializeValue(value), providerName, providerKey, ct);

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

    // ── v0.3.0 类型化序列化/反序列化映射 + 写时校验 ──

    /// <summary>类型化序列化（写）：bool → "true"/"false"（规范小写）；int/long/decimal/double → InvariantCulture；
    /// DateTime → ISO8601（"O"）；string 原样；其他（含 Json 对象/数组/record）→ JsonSerializer。
    /// null（引用类型）→ 空字符串（String 特征恒过校验；Json 特征由写时校验拒绝空文档）。</summary>
    private static string SerializeValue<T>(T value)
    {
        if (value is null) return string.Empty;
        if (value is bool b) return b ? "true" : "false";
        if (value is int i) return i.ToString(CultureInfo.InvariantCulture);
        if (value is long l) return l.ToString(CultureInfo.InvariantCulture);
        if (value is decimal m) return m.ToString(CultureInfo.InvariantCulture);
        if (value is double d) return d.ToString(CultureInfo.InvariantCulture);
        if (value is DateTime dt) return dt.ToString("O", CultureInfo.InvariantCulture);
        if (value is string s) return s;
        return JsonSerializer.Serialize(value);
    }

    /// <summary>类型化反序列化（读）：bool/int/long/decimal/double 用 TryParse（InvariantCulture，
    /// DateTime 用 RoundtripKind——ISO8601 可往返）；string 原样；其他 → JsonSerializer.Deserialize。
    /// 一律 fail-closed：解析失败 → defaultValue（Json 引用类型解析失败返回 null——P2 裁定），不抛异常。</summary>
    private static T DeserializeValue<T>(string raw, T defaultValue, ILogger logger)
    {
        var type = typeof(T);
        if (type == typeof(bool))
            return bool.TryParse(raw, out var b) ? (T)(object)b : defaultValue;
        if (type == typeof(int))
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? (T)(object)i : defaultValue;
        if (type == typeof(long))
            return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? (T)(object)l : defaultValue;
        if (type == typeof(decimal))
            return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var m) ? (T)(object)m : defaultValue;
        if (type == typeof(double))
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (T)(object)d : defaultValue;
        if (type == typeof(DateTime))
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? (T)(object)dt : defaultValue;
        if (type == typeof(string))
            return (T)(object)raw;

        try
        {
            return JsonSerializer.Deserialize<T>(raw) ?? defaultValue;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Feature 值 JSON 反序列化失败（返回默认值）| Type: {Type} | Value: {Value}",
                type.FullName, raw);
            return defaultValue;
        }
    }

    /// <summary>写时校验（对齐定义 ValueType）：Boolean→bool.TryParse；Int→int.TryParse（InvariantCulture）；
    /// Decimal→decimal.TryParse；DateTime→DateTime.TryParse（RoundtripKind——ISO8601 可往返）；
    /// Json→JsonDocument.TryParse（合法 JSON 文档）；String→恒通过（长度由列限制兜底）。
    /// <b>未定义 Feature → 跳过校验</b>（v0.2.0 无定义可写语义向后兼容）。违反 → <see cref="ArgumentException"/>。</summary>
    private void ValidateValueForDefinition(string name, string value)
    {
        var definition = _definitionRepository.GetAll().FirstOrDefault(d => d.Name == name);
        if (definition == null)
            return;

        switch (definition.ValueType)
        {
            case FeatureValueType.Boolean:
                if (!bool.TryParse(value, out _))
                    throw new ArgumentException($"Feature 值校验失败：{name} 为 Boolean 类型，值 \"{value}\" 非法（仅接受 true/false）", nameof(value));
                break;
            case FeatureValueType.Int:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new ArgumentException($"Feature 值校验失败：{name} 为 Int 类型，值 \"{value}\" 非法", nameof(value));
                break;
            case FeatureValueType.Decimal:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    throw new ArgumentException($"Feature 值校验失败：{name} 为 Decimal 类型，值 \"{value}\" 非法", nameof(value));
                break;
            case FeatureValueType.DateTime:
                if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
                    throw new ArgumentException($"Feature 值校验失败：{name} 为 DateTime 类型，值 \"{value}\" 非法（须可往返 ISO8601）", nameof(value));
                break;
            case FeatureValueType.Json:
                if (!IsValidJson(value))
                    throw new ArgumentException($"Feature 值校验失败：{name} 为 Json 类型，值不是合法 JSON 文档", nameof(value));
                break;
            case FeatureValueType.String:
            default:
                break;   // String 恒通过（长度由列限制兜底）
        }
    }

    /// <summary>Json 文档合法性检查（JsonDocument.TryParse——合法 JSON 文档）。</summary>
    private static bool IsValidJson(string value)
    {
        try
        {
            JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
