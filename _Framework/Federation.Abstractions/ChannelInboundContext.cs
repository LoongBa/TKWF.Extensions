using System.Collections.Generic;

namespace TKWF.Ext.Federation;

/// <summary>
/// 入站报文上下文（传输层——原始 body / query / 路径参数；由装配层构造，不承载业务语义）。
/// <para>替代 <c>SsoChannelEventContext</c>（I2 传输/语义分层，方案 §3.2 R2）——字段完全一致（纯改名），
/// 语义收窄：只允许放协议原始字段（<c>__raw</c>、<c>msg_signature</c>、<c>timestamp</c>、<c>nonce</c>、
/// <c>echostr</c>、<c>channel_id</c>）。<b>禁止</b>再往里写业务语义（<c>uid</c>/<c>msg_type</c>/<c>event</c>/
/// <c>__decrypted_xml</c> 全部迁到 <see cref="ChannelEvent"/>）。</para>
/// <para>同时服务多个 DIM 方法：<see cref="ISsoChannel.HandleEventAsync"/> / <see cref="ISsoChannel.VerifyUrlAsync"/> /
/// （未来）其他入站协议。</para>
/// </summary>
public sealed record ChannelInboundContext(IReadOnlyDictionary<string, string?> Parameters);