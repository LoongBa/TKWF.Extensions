using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 冻结状态读取提供者实现（V0.11.0 Wave 1c——方案 §4.6 D3）——读路径经 <see cref="IFreezeService"/> 取活跃行
/// （活跃判据单点，零重复谓词）；本类只加<b>帧内快照</b>（<see cref="Dictionary{TKey,TValue}"/> per 实例）。
/// <para>红线合规：零 IFreeSql/IEntityDAC/DataService 直触——数据面完全经领域门面。</para>
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。
/// </summary>
[DiContractIgnore]
internal sealed class FreezeStateProvider : DomainServiceBase, IFreezeStateProvider
{
    /// <summary>帧内快照（uid → 活跃冻结行）——实例随帧作用域，故绝不跨请求；空列表亦缓存（"未冻结"同样免重复查库）。</summary>
    private readonly Dictionary<string, IReadOnlyList<AuthAccountFreezeEntity>> _snapshot = new(StringComparer.Ordinal);

    private IFreezeService? _freezeService;
    private IFreezeService FreezeService => _freezeService ??= User.Use<IFreezeService>();

    public FreezeStateProvider(IDomainUser user) : base(user)
    {
    }

    /// <inheritdoc />
    public async Task<bool> IsFrozenAsync(string uid, int? scope = null, CancellationToken ct = default)
    {
        var active = await LoadAsync(uid, ct);
        if (active.Count == 0)
            return false;

        if (scope is null)
            return true;

        foreach (var row in active)
        {
            if (row.Scope == scope.Value)
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuthAccountFreezeEntity>> GetActiveAsync(string uid, CancellationToken ct = default)
        => LoadAsync(uid, ct);

    /// <inheritdoc />
    public void Invalidate(string uid)
    {
        if (!string.IsNullOrWhiteSpace(uid))
            _snapshot.Remove(uid);
    }

    /// <summary>取活跃行（命中快照即返回——含空结果；未命中经门面加载并写入快照）。</summary>
    private async Task<IReadOnlyList<AuthAccountFreezeEntity>> LoadAsync(string uid, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        if (_snapshot.TryGetValue(uid, out var cached))
            return cached;

        var active = await FreezeService.ListActiveFreezesAsync(uid, ct);
        _snapshot[uid] = active;
        return active;
    }
}
