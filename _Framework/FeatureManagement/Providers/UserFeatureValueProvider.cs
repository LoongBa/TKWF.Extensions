using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>User 层 Provider（最优先——providerKey=UserId）。</summary>
[DiContractIgnore]
internal sealed class UserFeatureValueProvider : DomainServiceBase, IFeatureValueProvider
{
    private IFeatureValueStore? _store;

    // ADR92/DI004：IFeatureValueStore 懒加载经基类 User.Use（TryAddEnumerableConstructible 帧内供给）
    private IFeatureValueStore Store => _store ??= User.Use<IFeatureValueStore>();

    public UserFeatureValueProvider(IDomainUser user) : base(user)
    {
    }

    public string Name => FeatureProviders.User;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await Store.GetAsync(name, FeatureProviders.User, providerKey, ct))?.Value;
}
