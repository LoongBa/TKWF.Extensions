using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 多原因冻结服务（V0.11.0 Wave 1b 门面层——方案 §4.6 D3）——冻结行集合替代 AuthAccount.IsFrozen/FreezeEnd
/// 单槽；多原因可并存、按原因部分解除、原因留痕。
/// 活跃判据：<c>ReleasedAt == null &amp;&amp; (FreezeEnd == null || FreezeEnd &gt; DateTime.UtcNow)</c>。
/// </summary>
public interface IFreezeService : IDomainService
{
    /// <summary>冻结（新行；<paramref name="freezeEnd"/> null = 永久直至显式解冻；幂等——同 (UId, Reason, Scope) 活跃行不重复建）。</summary>
    Task<AuthAccountFreezeEntity> FreezeAsync(string uid, string reason, DateTime? freezeEnd = null, string? note = null, string source = "manual", CancellationToken ct = default);

    /// <summary>按原因释放（置 ReleasedAt/ReleasedBy；<paramref name="reason"/> 必填——部分解除语义单点，空白拒绝）。</summary>
    Task ReleaseFreezeAsync(string uid, string reason, string? operatorName = null, CancellationToken ct = default);

    /// <summary>释放全部活跃冻结（置 ReleasedAt/ReleasedBy；无活跃行幂等）。</summary>
    Task ReleaseAllFreezesAsync(string uid, string? operatorName = null, CancellationToken ct = default);

    /// <summary>是否冻结（活跃判据；可按 Scope 过滤——V0.11.0 恒为 0，其余为预留位）。</summary>
    Task<bool> IsFrozenAsync(string uid, int? scope = null, CancellationToken ct = default);

    /// <summary>查询活跃冻结行（ReleasedAt null 且未到期）。</summary>
    Task<IReadOnlyList<AuthAccountFreezeEntity>> ListActiveFreezesAsync(string uid, CancellationToken ct = default);
}
