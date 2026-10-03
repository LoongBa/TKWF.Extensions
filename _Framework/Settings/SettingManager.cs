using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置管理器实现——分层读写（User → Tenant → Global → 默认值）+ 内存缓存。
    /// <para>V0.3.0（领域自治根治，正确路线）：继承 <see cref="DomainServiceBase"/>（与业务领域开发方式一致），
    /// 经基类 <see cref="DomainServiceBase.User"/> 获取用户上下文——IDomainUser 永不注册 DI（D01 领域自治，
    /// 构造注入 IDomainUser 而 IDomainUser 不进 DI 正是 v0.3.3 故障根因）。</para>
    /// <para>删除 ISettingStore/SettingStore 伪 DataService 层（职责与 <see cref="SettingEntityDataService"/> 完全重叠），
    /// 直接经 <c>User.Use&lt;SettingEntityDataService&gt;()</c> 懒加载 DataService（NoAop 路径，ActivatorUtilities 直建，
    /// IEntityDAC 从 DI 解析——数据访问红线合规，零 ORM/零 IEntityDAC 直接注入）。</para>
    /// <para>注册形态由 TryAddScoped 改为 <c>AddConstructibleService&lt;ISettingManager, SettingManager&gt;</c>——
    /// 接口构造工厂 + CurrentAopUser 守卫（构造注入链在域作用域外解析即抛 InvalidOperationException，DI004 编译期门控零豁免）。</para>
    /// <para>异常静默保留（对齐 UserCenter §5.3 降级矩阵仓库惯例）：数据访问失败记录 Warning，不阻塞业务调用。</para>
    /// </summary>
    internal sealed class SettingManager : DomainServiceBase, ISettingManager
    {
        private const string UserProvider = "User";
        private const string TenantProvider = "Tenant";
        private const string GlobalProvider = "Global";
        private const string CacheKeyPrefix = "Setting:";
        private const string NotFoundSentinel = "\x02NOTFOUND\x02";

        private readonly SettingsOptions _options;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SettingManager> _logger;

        private SettingEntityDataService? _dataService;
        private SettingEntityDataService DataService => _dataService ??= User.Use<SettingEntityDataService>();

        public SettingManager(
            IDomainUser user,
            IOptions<SettingsOptions> options,
            IMemoryCache cache,
            ILogger<SettingManager> logger)
            : base(user)
        {
            _options = options?.Value ?? new SettingsOptions();
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<string> GetAsync(string name, string defaultValue = "", CancellationToken ct = default)
        {
            // 匿名用户跳过 User/Tenant 层，直接查 Global → 默认值
            if (!User.IsAuthenticated)
            {
                return await GetFromGlobalOrCacheAsync(name, defaultValue, ct);
            }

            // 分层回退：User → Tenant → Global → 默认值
            var userId = User.UserId;
            if (!string.IsNullOrEmpty(userId))
            {
                var (found, value) = await TryGetFromLayerAsync(name, UserProvider, userId, ct);
                if (found)
                    return value;
            }

            var tenantId = User.TenantId;
            if (tenantId.HasValue)
            {
                var (found, value) = await TryGetFromLayerAsync(name, TenantProvider, tenantId.Value.ToString(), ct);
                if (found)
                    return value;
            }

            return await GetFromGlobalOrCacheAsync(name, defaultValue, ct);
        }

        /// <inheritdoc />
        public async Task<T> GetAsync<T>(string name, T defaultValue = default!, CancellationToken ct = default)
        {
            var json = await GetAsync(name, "", ct);
            if (string.IsNullOrEmpty(json))
                return defaultValue;

            try
            {
                return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? defaultValue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "设置反序列化失败: Name={Name}", name);
                return defaultValue;
            }
        }

        /// <inheritdoc />
        public async Task SetAsync(string name, string value, CancellationToken ct = default)
        {
            string providerName;
            string? providerKey;

            if (User.IsAuthenticated && !string.IsNullOrEmpty(User.UserId))
            {
                providerName = UserProvider;
                providerKey = User.UserId;
            }
            else
            {
                providerName = GlobalProvider;
                providerKey = null;
            }

            try
            {
                var now = DateTimeOffset.Now;
                var entity = new SettingEntity
                {
                    Name = name,
                    Value = value,
                    ProviderName = providerName,
                    ProviderKey = providerKey,
                    Description = null,
                    IsVisibleToClients = true,
                    CreateTime = now,
                    UpdateTime = now
                };
                await DataService.UpsertByKeyAsync(entity, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "设置写入失败: Name={Name}, Provider={ProviderName}/{ProviderKey}", name, providerName, providerKey);
            }

            InvalidateCacheForName(name);
        }

        /// <inheritdoc />
        public async Task SetAsync<T>(string name, T value, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(value);
            await SetAsync(name, json, ct);
        }

        /// <summary>
        /// 尝试从指定层读取设置值（带缓存）。
        /// 返回 (true, value) 表示该层有值；返回 (false, "") 表示该层无此设置。
        /// </summary>
        private async Task<(bool Found, string Value)> TryGetFromLayerAsync(string name, string providerName, string providerKey, CancellationToken ct)
        {
            var cacheKey = BuildCacheKey(providerName, providerKey, name);

            if (_cache.TryGetValue(cacheKey, out string? cached))
            {
                if (cached == NotFoundSentinel)
                    return (false, "");

                return (true, cached!);
            }

            var entity = await TryReadAsync(name, providerName, providerKey, ct);

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromSeconds(_options.CacheExpirationSeconds));

            if (entity?.Value is null)
            {
                _cache.Set(cacheKey, NotFoundSentinel, cacheOptions);
                return (false, "");
            }

            _cache.Set(cacheKey, entity.Value, cacheOptions);
            return (true, entity.Value);
        }

        /// <summary>
        /// 从 Global 层读取设置值（带缓存），未命中时返回 defaultValue。
        /// </summary>
        private async Task<string> GetFromGlobalOrCacheAsync(string name, string defaultValue, CancellationToken ct)
        {
            var cacheKey = BuildCacheKey(GlobalProvider, providerKey: null, name);

            if (_cache.TryGetValue(cacheKey, out string? cached))
            {
                return cached == NotFoundSentinel ? defaultValue : cached!;
            }

            var entity = await TryReadAsync(name, GlobalProvider, providerKey: null, ct);

            var result = entity?.Value;

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromSeconds(_options.CacheExpirationSeconds));

            if (result is null)
            {
                _cache.Set(cacheKey, NotFoundSentinel, cacheOptions);
                return defaultValue;
            }

            _cache.Set(cacheKey, result, cacheOptions);
            return result;
        }

        /// <summary>
        /// 读取单条设置——异常静默（对齐仓库降级矩阵惯例）：失败记录 Warning 返回 null，不阻塞业务调用。
        /// </summary>
        private async Task<SettingEntity?> TryReadAsync(string name, string providerName, string? providerKey, CancellationToken ct)
        {
            try
            {
                return await DataService.GetByKeyAsync(name, providerName, providerKey, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "设置读取失败: Name={Name}, Provider={ProviderName}/{ProviderKey}", name, providerName, providerKey);
                return null;
            }
        }

        /// <summary>清除指定设置名称在所有层的缓存。</summary>
        private void InvalidateCacheForName(string name)
        {
            if (User.IsAuthenticated && !string.IsNullOrEmpty(User.UserId))
                _cache.Remove(BuildCacheKey(UserProvider, User.UserId, name));

            if (User.TenantId.HasValue)
                _cache.Remove(BuildCacheKey(TenantProvider, User.TenantId.Value.ToString(), name));

            _cache.Remove(BuildCacheKey(GlobalProvider, providerKey: null, name));
        }

        /// <summary>构建缓存 key：Setting:{ProviderName}:{ProviderKey}:{Name}</summary>
        private static string BuildCacheKey(string providerName, string? providerKey, string name)
            => $"{CacheKeyPrefix}{providerName}:{providerKey ?? ""}:{name}";
    }
}
