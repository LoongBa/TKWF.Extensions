using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.WeChatChannels;

/// <summary>
/// 通道在用守卫契约（删除通道前由 <c>IChannelConfigService.DeleteChannelAsync</c> 枚举检查——任一实现报告
/// <see cref="ChannelUsageCheck.InUse"/> 即拒绝物理删）。
/// <para><b>可选 SPI</b>：WeChatChannels 主包（平台模式）不感知运营数据；运营数据属主（如 WeChatOperations）实现本契约
/// 并经 <c>TryAddEnumerableConstructible&lt;IChannelUsageGuard, X&gt;()</c> 注册，即可阻止删除仍被引用的通道
/// （对齐 <c>IEventReplyHandler</c> 消费方实现形态——零实现时删除放行，平台独用不受影响）。</para>
/// </summary>
public interface IChannelUsageGuard : IDomainService
{
    /// <summary>检查通道是否在用（存在会因删除而悬空的引用时返回 InUse）。</summary>
    Task<ChannelUsageCheck> CheckAsync(string channelId, CancellationToken ct = default);
}

/// <summary>通道在用检查结果（<see cref="Reason"/> 供拒绝消息说明引用来源）。</summary>
public sealed record ChannelUsageCheck(bool InUse, string? Reason = null);
