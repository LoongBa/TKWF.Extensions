using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Role 层 Provider（providerKey=角色名——Manager 遍历 Roles 逐个调用，首个命中回退）。</summary>
internal sealed class RoleFeatureValueProvider : IFeatureValueProvider
{
    private readonly IDomainUser _user;
    private IFeatureValueStore? _store;

    // ADR88/DI004：IFeatureValueStore 懒加载经 IDomainUser.Use<IDomainService>()
    private IFeatureValueStore Store => _store ??= _user.Use<IFeatureValueStore>();

    public RoleFeatureValueProvider(IDomainUser user) => _user = user ?? throw new ArgumentNullException(nameof(user));

    public string Name => FeatureProviders.Role;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await Store.GetAsync(name, FeatureProviders.Role, providerKey, ct))?.Value;
}
