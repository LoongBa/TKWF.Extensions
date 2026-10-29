using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>
/// 运营配置只读查询契约（D03 1.10——方案 §10.3 定稿：进 FA 承载，零新契约包；WeChatOperations 实现 DB 源）。
/// <para>设计动机：Federation 消费降级/文案/TicketMode 时经本接口桥接，<b>不直接跨扩展依赖 WeChatOperations</b>
/// （且反向依赖不可接受——L2 门控）；纯查询 + 默认静态降级（未配置 → null → 消费方 Options 兜底）。</para>
/// <para>实现：WeChatOperations 主包 <c>WeChatOperationsConfigQuery</c>（DB 权威 + IMemoryCache + 三机制失效），
/// 消费方经 <c>User.Use&lt;IOperationsConfigQuery&gt;()</c> 帧内解析（DI004——契约在 FA、实现在 WeChatOperations）。</para>
/// </summary>
public interface IOperationsConfigQuery : IDomainService
{
    /// <summary>图文回复 URL 模板（D03 §7.1——白名单占位符 <c>{ticket}/{uid}/{openid}/{channel_id}/{channel_type}/{channel_alias}</c>；
    /// null = 未配置 → 消费方回退内置测试端点 URL）。</summary>
    Task<string?> GetArticleReplyUrlTemplateAsync(CancellationToken ct = default);

    /// <summary>回复文案配置（S3 停用提示 + S5 兜底——null = 未配置 → 消费方 Options 降级默认）。</summary>
    Task<ReplyTextConfigDto?> GetReplyTextConfigAsync(CancellationToken ct = default);

    /// <summary>S2 未知通道提示开关（D03 §4.3 S2——null = 未配置 → 消费方默认 false 静默防探测）。</summary>
    Task<bool?> GetUnknownChannelReplyEnabledAsync(CancellationToken ct = default);

    /// <summary>票据行为配置（Mode/Ttl——null = 未配置 → 消费方默认 Sealed/300）。</summary>
    Task<TicketConfigDto?> GetTicketConfigAsync(CancellationToken ct = default);
}

/// <summary>回复文案配置 DTO（S3 停用提示 + S5 兜底；<c>Version</c> 供消费方缓存冲突决策）。</summary>
public sealed record ReplyTextConfigDto(string? DisabledChannelReplyTemplate, string? DefaultReplyText, long Version);

/// <summary>票据行为配置 DTO（<c>Mode</c>：Sealed | Loose——仅 Sealed 本期可用；<c>Version</c> 同前）。</summary>
public sealed record TicketConfigDto(string? Mode, int? TtlSeconds, long Version);