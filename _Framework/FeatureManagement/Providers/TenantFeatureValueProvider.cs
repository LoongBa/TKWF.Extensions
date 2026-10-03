using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Tenant 层 Provider（仅 TenantId.HasValue 时调用——providerKey=TenantId 字符串）。</summary>
internal sealed class TenantFeatureValueProvider : IFeatureValueProvider
{
    private readonly IDomainUser _user;
    private IFeatureValueStore? _store;

    // ADR88/DI004：IFeatureValueStore 懒加载经 IDomainUser.Use<IDomainService>()
    private IFeatureValueStore Store => _store ??= _user.Use<IFeatureValueStore>();

    public TenantFeatureValueProvider(IDomainUser user) => _user = user ?? throw new ArgumentNullException(nameof(user));

    public string Name => FeatureProviders.Tenant;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await Store.GetAsync(name, FeatureProviders.Tenant, providerKey, ct))?.Value;
}
