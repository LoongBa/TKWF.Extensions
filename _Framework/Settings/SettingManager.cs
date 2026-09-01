using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置管理器实现——分层读写（User → Tenant → Global → 默认值）。
    /// <para>当前 V0.1.0 实现仅支持 Global 层；User/Tenant 分层留 V0.2.0。</para>
    /// </summary>
    internal sealed class SettingManager : ISettingManager
    {
        private readonly ISettingStore _store;
        private readonly IDomainUser _user;
        private readonly SettingsOptions _options;

        public SettingManager(ISettingStore store, IDomainUser user, IOptions<SettingsOptions> options)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _options = options?.Value ?? new SettingsOptions();
        }

        /// <inheritdoc />
        public async Task<string> GetAsync(string name, string defaultValue = "", CancellationToken ct = default)
        {
            // V0.1.0：仅 Global 层
            var entity = await _store.GetAsync(name, _options.DefaultSettingValueProvider, providerKey: null, ct);
            return entity?.Value ?? defaultValue;
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
            catch
            {
                return defaultValue;
            }
        }

        /// <inheritdoc />
        public async Task SetAsync(string name, string value, CancellationToken ct = default)
        {
            // V0.1.0：写入 Global 层
            await _store.SetAsync(name, value, _options.DefaultSettingValueProvider, providerKey: null, description: null, ct);
        }

        /// <inheritdoc />
        public async Task SetAsync<T>(string name, T value, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(value);
            await SetAsync(name, json, ct);
        }
    }
}
