using System;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Global 层 Provider（最不优先兜底层——providerKey 忽略；匿名直查层）。</summary>
internal sealed class GlobalFeatureValueProvider : IFeatureValueProvider
{
    private readonly IFeatureValueStore _store;

    public GlobalFeatureValueProvider(IFeatureValueStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Name => FeatureProviders.Global;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await _store.GetAsync(name, FeatureProviders.Global, null, ct))?.Value;
}
