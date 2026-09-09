using System;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Tenant 层 Provider（仅 TenantId.HasValue 时调用——providerKey=TenantId 字符串）。</summary>
internal sealed class TenantFeatureValueProvider : IFeatureValueProvider
{
    private readonly IFeatureValueStore _store;

    public TenantFeatureValueProvider(IFeatureValueStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Name => FeatureProviders.Tenant;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await _store.GetAsync(name, FeatureProviders.Tenant, providerKey, ct))?.Value;
}
