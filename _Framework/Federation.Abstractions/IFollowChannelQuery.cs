using System;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Federation;

/// <summary>向平台反查当前关注状态（I2 §3.5——状态修复/对账用，<b>不在事件路径上调用</b>：事件本身就是真相）。</summary>
public sealed record FollowStateSnapshot(bool IsFollowing, DateTime? SubscribedAt, DateTime? UnsubscribedAt);

/// <summary>
/// 关注状态反查契约（I2 渠道关注关系域 §3.5）。
/// <para>实现者：平台通道（如 <c>WeChatEventChannel</c> 经 <c>GET /cgi-bin/user/info</c> 的 <c>subscribe</c> 字段）；
/// 平台无此能力时<b>不应实现本接口</b>（配合 <see cref="ChannelCapabilities.FollowQuery"/> 声明）。</para>
/// <para>注册：与通道同批（<c>Add{Platform}FederationChannels</c> 内 TryAddEnumerableConstructible——
/// 消费方按 <see cref="ChannelCapabilities.FollowQuery"/> 判定可用性）。</para>
/// <para>用途：状态修复 / 对账；平台 API 失败 → 返回 null（调用方自行处置——不打 try/catch 兜底）。</para>
/// </summary>
public interface IFollowChannelQuery
{
    /// <summary>向平台反查当前关注状态；平台无此能力/API 失败 → null。</summary>
    Task<FollowStateSnapshot?> QueryFollowStateAsync(string externalUserId, CancellationToken ct = default);
}
