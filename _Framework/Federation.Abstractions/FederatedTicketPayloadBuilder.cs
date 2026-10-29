using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace TKWF.Ext.Federation;

/// <summary>
/// 签票侧 payload 组装器（1.3 载荷切换——LoginEntryService / EventReplyHandlerBase 共用单点，测试锁定字段映射）。
/// <para>组装语义（D03 §5.1a/条件 4）：<c>provider</c> 取自通道平台（wechat）；<c>channelType</c> 为<b>通道形态</b>
/// （mp_service/mp_subscription...——Caller 传入或经 <see cref="Resolver"/> 从协议类型回退）；<b>协议通道类型</b>
/// （wechat_oauth/wechat_event）经 <see cref="FederatedTicketPayloadExtensions.ProtocolTypeKey"/> 双写进 Extensions——
/// 兑付侧 <see cref="FederatedTicketPayloadExtensions.ToFederatedIdentity"/> 优先取回，缺失按形态回退表推断。</para>
/// <para>⚠️ 命名碰撞红线（Oracle6 条件 4）：<c>channelForm</c> 参数是通道形态；协议类型应经
/// <see cref="FederatedTicketPayloadExtensions.ProtocolTypeKey"/> 传入，勿传错槽位（<see cref="ChannelTypeRegistry.AssertLegal"/>
/// 会拦截误传的 wechat_oauth——fail-fast 早暴露）。</para>
/// </summary>
public static class FederatedTicketPayloadBuilder
{
    /// <summary>
    /// 协议通道类型 → 默认通道形态回退映射（仅当通道配置未存 ChannelForm（存量）时使用——形态推断口径，
    /// 与 <see cref="FederatedTicketPayloadExtensions.ProtocolTypeFallback"/> 成对互逆，测试锁定）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ChannelFormFallback =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["wechat_event"] = "mp_subscription",   // 事件推送典型场景（订阅号/服务号均可，取最安全默认）
            ["wechat_oauth"] = "mp_service",        // 网页授权典型场景（服务号）
        };

    /// <summary>组装回调（行内解析——由调用方传入通道配置解析逻辑，保持本类零 DI 纯静态）。</summary>
    /// <param name="channelId">通道不变键。</param>
    /// <returns>解析结果：ChannelForm 通道形态（可空 → 回退映射）；ChannelAlias 对外别名（可空）；Provider 平台族（缺省 "wechat"）。</returns>
    public delegate (string? ChannelForm, string? ChannelAlias, string Provider) ResolverDelegate(string channelId);

    /// <summary>
    /// 组装联邦免登票据载荷。
    /// </summary>
    /// <param name="provider">平台族（wechat/alipay/...；<paramref name="resolver"/> 提供时以其返回为准）。</param>
    /// <param name="protocolChannelType">协议通道类型（wechat_oauth/wechat_event——双写入 Extensions）。</param>
    /// <param name="channelId">通道不变键。</param>
    /// <param name="externalUserId">外部用户标识（openid）。</param>
    /// <param name="channelForm">通道形态（可空——回退 <paramref name="protocolChannelType"/> 映射）。</param>
    /// <param name="channelAlias">通道对外别名（可空）。</param>
    /// <param name="unionId">跨通道归并键（可空）。</param>
    /// <param name="platformUid">L2 平台身份（可空）。</param>
    /// <param name="assurance">档案可信等级（event/base/userinfo——缺省 "event"）。</param>
    /// <param name="ttl">票据 TTL（正数——决定 Exp）。</param>
    /// <param name="extensions">附加扩展（调用方已有键合入；protocol_type/is_test 由本类规范化）。</param>
    /// <param name="resolver">通道配置解析（可空——不查 registry，直接使用入参）。</param>
    public static FederatedTicketPayload Build(
        string provider,
        string protocolChannelType,
        string channelId,
        string externalUserId,
        string? channelForm = null,
        string? channelAlias = null,
        string? unionId = null,
        string? platformUid = null,
        string assurance = "event",
        TimeSpan? ttl = null,
        IReadOnlyDictionary<string, string?>? extensions = null,
        ResolverDelegate? resolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalUserId);

        if (resolver is not null)
        {
            var (resolvedForm, resolvedAlias, resolvedProvider) = resolver(channelId);
            provider = resolvedProvider ?? provider;
            channelForm = resolvedForm ?? channelForm;
            channelAlias = resolvedAlias ?? channelAlias;
        }

        // 形态回退（存量通道未配 ChannelForm → 协议类型映射；未映射 → AssertLegal 将拦截）
        channelForm ??= ChannelFormFallback.TryGetValue(protocolChannelType, out var f) ? f : null;
        ChannelTypeRegistry.AssertLegal(provider, channelForm ?? string.Empty);

        var now = DateTimeOffset.UtcNow;
        var ttlSeconds = Math.Max((int)(ttl?.TotalSeconds ?? 300), 1);
        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (extensions is not null)
        {
            foreach (var (k, v) in extensions)
                merged[k] = v;
        }
        merged[FederatedTicketPayloadExtensions.ProtocolTypeKey] = protocolChannelType;

        return new FederatedTicketPayload(
            Provider: provider,
            ChannelType: channelForm!,
            ChannelId: channelId,
            ChannelAlias: channelAlias,
            ExternalUserId: externalUserId,
            UnionId: unionId,
            PlatformUid: platformUid,
            Assurance: assurance,
            Iat: now.ToUnixTimeSeconds(),
            Exp: now.AddSeconds(ttlSeconds).ToUnixTimeSeconds(),
            Jti: NewJti())
        {
            Extensions = merged,
        };
    }

    /// <summary>单次标识生成（16 字节 CSPRNG base64url——32 字符，防重放判据；jti 预算注释 R9）。</summary>
    private static string NewJti()
    {
        Span<byte> buf = stackalloc byte[16];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToBase64String(buf).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}