using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道特定事件回复处理器（W3 多通道 handler 绑定缝——2026-10-16，两级模型第二级扩展）。
/// <para>可选标记接口——<b>不改 <see cref="IEventReplyHandler"/> 契约</b>（消费方既有实现零破坏）。
/// 实现本接口的 handler 声明 <see cref="ChannelId"/>：编排解析序 = <b>精确 channelId 绑定桶优先</b>
/// （实现本接口且 <c>ChannelId</c> 匹配事件通道的 handler，桶内 first-non-null-wins）→
/// <b>ChannelType 桶回退</b>（<see cref="IEventReplyHandler.ChannelType"/> 匹配——现状语义）。</para>
/// <para>默认 handler（如 <see cref="TestEventReplyHandler"/>）<b>不实现</b>本接口 = 服务所有同类型通道
/// （默认共用）；实现本接口 = 为特定 channelId 覆盖（<b>后续扩展</b>——本期提供契约缝 + 编排支持，
/// 消费方按需实现）。</para>
/// </summary>
public interface IChannelSpecificEventReplyHandler : IEventReplyHandler
{
    /// <summary>绑定的通道实例 id（精确匹配——优先于 <see cref="IEventReplyHandler.ChannelType"/> 桶）。</summary>
    string ChannelId { get; }
}
