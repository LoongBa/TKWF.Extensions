using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>User 层 Provider（最优先——providerKey=UserId）。</summary>
internal sealed class UserFeatureValueProvider : IFeatureValueProvider
{
    private readonly IDomainUser _user;
    private IFeatureValueStore? _store;

    // ADR88/DI004：IFeatureValueStore 懒加载经 IDomainUser.Use<T>()
    private IFeatureValueStore Store => _store ??= _user.Use<IFeatureValueStore>();

    public UserFeatureValueProvider(IDomainUser user)
        => _user = user ?? throw new ArgumentNullException(nameof(user));

    public string Name => FeatureProviders.User;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await Store.GetAsync(name, FeatureProviders.User, providerKey, ct))?.Value;
}
