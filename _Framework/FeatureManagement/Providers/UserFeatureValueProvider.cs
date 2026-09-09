using System;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FeatureManagement;

/// <summary>User 层 Provider（最优先——providerKey=UserId）。</summary>
internal sealed class UserFeatureValueProvider : IFeatureValueProvider
{
    private readonly IFeatureValueStore _store;

    public UserFeatureValueProvider(IFeatureValueStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Name => FeatureProviders.User;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await _store.GetAsync(name, FeatureProviders.User, providerKey, ct))?.Value;
}
