using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Tenant 层 Provider（仅 TenantId.HasValue 时调用——providerKey=TenantId 字符串）。</summary>
[DiContractIgnore]
internal sealed class TenantFeatureValueProvider : DomainServiceBase, IFeatureValueProvider
{
    private IFeatureValueStore? _store;

    // ADR92/DI004：IFeatureValueStore 懒加载经基类 User.Use（TryAddEnumerableConstructible 帧内供给）
    private IFeatureValueStore Store => _store ??= User.Use<IFeatureValueStore>();

    public TenantFeatureValueProvider(IDomainUser user) : base(user)
    {
    }

    public string Name => FeatureProviders.Tenant;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await Store.GetAsync(name, FeatureProviders.Tenant, providerKey, ct))?.Value;
}
