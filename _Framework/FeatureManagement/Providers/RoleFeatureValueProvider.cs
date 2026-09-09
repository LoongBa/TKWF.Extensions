using System;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Role 层 Provider（providerKey=角色名——Manager 遍历 Roles 逐个调用，首个命中回退）。</summary>
internal sealed class RoleFeatureValueProvider : IFeatureValueProvider
{
    private readonly IFeatureValueStore _store;

    public RoleFeatureValueProvider(IFeatureValueStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Name => FeatureProviders.Role;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await _store.GetAsync(name, FeatureProviders.Role, providerKey, ct))?.Value;
}
