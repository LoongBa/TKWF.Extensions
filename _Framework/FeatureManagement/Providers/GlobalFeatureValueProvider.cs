using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Global 层 Provider（最不优先兜底层——providerKey 忽略；匿名直查层）。</summary>
[DiContractIgnore]
internal sealed class GlobalFeatureValueProvider : DomainServiceBase, IFeatureValueProvider
{
    private FeatureValueEntityDataService? _store;

    // ADR92/DI004：IFeatureValueStore 懒加载经基类 User.Use（TryAddEnumerableConstructible 帧内供给）
    private FeatureValueEntityDataService Store => _store ??= User.Use<FeatureValueEntityDataService>();

    public GlobalFeatureValueProvider(IDomainUser user) : base(user)
    {
    }

    public string Name => FeatureProviders.Global;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await Store.GetAsync(name, FeatureProviders.Global, null, ct))?.Value;
}
