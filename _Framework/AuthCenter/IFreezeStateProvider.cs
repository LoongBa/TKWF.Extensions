using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 冻结状态读取提供者（V0.11.0 方案 §4.6 D3 / §五 T3）——<b>冻结判定的唯一读取入口</b>：替代随 T1 删除的
/// <c>AuthAccount.IsFrozen</c>/<c>FreezeEnd</c>/<c>IsFrozenEffective</c>（单槽派生属性 → 多原因冻结行集合）。
/// <para>活跃判据单点仍在 <see cref="IFreezeService"/>（<c>ReleasedAt == null &amp;&amp; (FreezeEnd == null || FreezeEnd &gt; UtcNow)</c>）；
/// 本类只负责<b>帧内（请求级）快照缓存</b>，使认证/签发/找回热路径不必重复查库。</para>
/// <para>缓存生命周期：门面实例 = 帧作用域（构造式服务，<c>AddConstructibleService</c>）——绝不跨请求；
/// 冻结行变更由写路径（<see cref="IFreezeService"/> 的 Freeze/Release*）调用 <see cref="Invalidate"/> 主动失效。</para>
/// </summary>
public interface IFreezeStateProvider : IDomainService
{
    /// <summary>是否冻结（活跃判据；<paramref name="scope"/> null = 任一原因命中，V0.11.0 恒 0）。</summary>
    Task<bool> IsFrozenAsync(string uid, int? scope = null, CancellationToken ct = default);

    /// <summary>当前活跃冻结行（CreatedAt 升序——供拒绝原因/运维展示；同帧快照）。</summary>
    Task<IReadOnlyList<AuthAccountFreezeEntity>> GetActiveAsync(string uid, CancellationToken ct = default);

    /// <summary>失效指定账号的同帧快照（冻结/释放写入后由写路径调用；未缓存/空白 uid 为无操作）。</summary>
    void Invalidate(string uid);
}
