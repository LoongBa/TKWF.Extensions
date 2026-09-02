using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置管理器实现——分层读写（User → Tenant → Global → 默认值）+ 内存缓存。
    /// <para>V0.2.0：完整分层（User → Tenant → Global → 默认值逐层回退）+ IMemoryCache 读缓存。
    /// 匿名用户（IsAuthenticated == false）跳过 User/Tenant 层，直接查 Global。</para>
    /// </summary>
    internal sealed class SettingManager : ISettingManager
    {
        private const string UserProvider = "User";
        private const string TenantProvider = "Tenant";
        private const string GlobalProvider = "Global";
        private const string CacheKeyPrefix = "Setting:";
        private const string NotFoundSentinel = "\x02NOTFOUND\x02";

        private readonly ISettingStore _store;
        private readonly IDomainUser _user;
        private readonly SettingsOptions _options;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SettingManager> _logger;

        public SettingManager(
            ISettingStore store,
            IDomainUser user,
            IOptions<SettingsOptions> options,
            IMemoryCache cache,
            ILogger<SettingManager> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _options = options?.Value ?? new SettingsOptions();
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<string> GetAsync(string name, string defaultValue = "", CancellationToken ct = default)
        {
            // 匿名用户跳过 User/Tenant 层，直接查 Global → 默认值
            if (!_user.IsAuthenticated)
            {
                return await GetFromGlobalOrCacheAsync(name, defaultValue, ct);
            }

            // 分层回退：User → Tenant → Global → 默认值
            var userId = _user.UserId;
            if (!string.IsNullOrEmpty(userId))
            {
                var (found, value) = await TryGetFromLayerAsync(name, UserProvider, userId, ct);
                if (found)
                    return value;
            }

            var tenantId = _user.TenantId;
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

            if (_user.IsAuthenticated && !string.IsNullOrEmpty(_user.UserId))
            {
                providerName = UserProvider;
                providerKey = _user.UserId;
            }
            else
            {
                providerName = GlobalProvider;
                providerKey = null;
            }

            await _store.SetAsync(name, value, providerName, providerKey, description: null, ct);
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

            var entity = await _store.GetAsync(name, providerName, providerKey, ct);

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

            var entity = await _store.GetAsync(name, GlobalProvider, providerKey: null, ct);

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

        /// <summary>清除指定设置名称在所有层的缓存。</summary>
        private void InvalidateCacheForName(string name)
        {
            if (_user.IsAuthenticated && !string.IsNullOrEmpty(_user.UserId))
                _cache.Remove(BuildCacheKey(UserProvider, _user.UserId, name));

            if (_user.TenantId.HasValue)
                _cache.Remove(BuildCacheKey(TenantProvider, _user.TenantId.Value.ToString(), name));

            _cache.Remove(BuildCacheKey(GlobalProvider, providerKey: null, name));
        }

        /// <summary>构建缓存 key：Setting:{ProviderName}:{ProviderKey}:{Name}</summary>
        private static string BuildCacheKey(string providerName, string? providerKey, string name)
            => $"{CacheKeyPrefix}{providerName}:{providerKey ?? ""}:{name}";
    }
}
