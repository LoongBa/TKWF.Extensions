using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Global 层 Provider（最不优先兜底层——providerKey 忽略；匿名直查层）。</summary>
internal sealed class GlobalFeatureValueProvider : IFeatureValueProvider
{
    private readonly IDomainUser _user;
    private IFeatureValueStore? _store;

    // ADR88/DI004：IFeatureValueStore 懒加载经 IDomainUser.Use<IDomainService>()
    private IFeatureValueStore Store => _store ??= _user.Use<IFeatureValueStore>();

    public GlobalFeatureValueProvider(IDomainUser user) => _user = user ?? throw new ArgumentNullException(nameof(user));

    public string Name => FeatureProviders.Global;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await Store.GetAsync(name, FeatureProviders.Global, null, ct))?.Value;
}
