using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Role 层 Provider（providerKey=角色名——Manager 遍历 Roles 逐个调用，首个命中回退）。</summary>
[DiContractIgnore]
internal sealed class RoleFeatureValueProvider : DomainServiceBase, IFeatureValueProvider
{
    private IFeatureValueStore? _store;

    // ADR92/DI004：IFeatureValueStore 懒加载经基类 User.Use（TryAddEnumerableConstructible 帧内供给）
    private IFeatureValueStore Store => _store ??= User.Use<IFeatureValueStore>();

    public RoleFeatureValueProvider(IDomainUser user) : base(user)
    {
    }

    public string Name => FeatureProviders.Role;

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await Store.GetAsync(name, FeatureProviders.Role, providerKey, ct))?.Value;
}
