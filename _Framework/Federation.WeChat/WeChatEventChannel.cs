using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信事件推送通道（`ISsoChannel: wechat_event`）——公众号事件推送身份获取（形态 B/未认证号）。
/// <para>设计文档 §4.4 形态 B：用户 发消息/点 CLICK 菜单 → 微信推送事件 → 装配层接收端点
/// （验签 + AES 解密的 <b>信任根</b> 由本通道执行）→ <c>FromUserName</c>=openid → 归一
/// <c>(channel_id, openid) → uid</c> → 编排层转 accesscode → 被动回复图文链接。</para>
/// <para><b>信任根一票否决（设计文档 §8.1）</b>：<see cref="WeChatEventCrypto.VerifySignature"/> 验 msg_signature
/// + <see cref="WeChatEventCrypto.DecryptMsg"/> AES 解密——漏验 = 可伪造任意 openid = 体系崩溃。
/// 验签失败返回 <see cref="SsoChannelAuthResult"/>（FailReason=WECHAT_SIGNATURE_INVALID）。</para>
/// <para>Context 参数（装配层接收端点传入——微信推送 URL query + body）：</para>
/// <para>① 安全模式验签：<c>Parameters["signature"]</c>/<c>["timestamp"]</c>/<c>["nonce"]</c>/<c>["msg_encrypt"]</c>；
/// ② 解密后 FromUserName 即 openid。</para>
/// <para>边界：本通道只管"验签+解密+取 openid"，不负责事件业务处理/被动回复（归装配层/编排层——
/// 设计文档 §9：各公众号后端事件接收器自实现，本库提供服务端验签解密能力）。</para>
/// <para>注册：库扩展方法 <c>AddWeChatFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92）；
/// channel 业务参数经 Options 注入不占 ctor IDomainUser 槽（SSO.WeChat §5.2 P1-3）。</para>
/// <para><b>事件链（外部身份联邦 E3——ADR-Federation-外部身份联邦与密封票据适配 E3）</b>：
/// <see cref="HandleEventAsync"/> 复用验签/解密链 + 提取 MsgType/Event 富化 Parameters（评审 F4）→
/// 委托 <see cref="IEventReplyHandler"/>（消费方实现，按 ChannelType=wechat_event 匹配，first-non-null-wins）
/// → 业务内容包<b>微信 XML 被动回复</b>（text/xml；<c>ToUserName</c>=原 FromUserName(openid)/
/// <c>FromUserName</c>=原 ToUserName(公众号)）——handler 委托为空返回 200 空回复（微信 5 秒超时兜底，
/// 异步客服消息机制归 Phase 2/消费方）。</para>
/// </summary>
[DiContractIgnore]
public sealed class WeChatEventChannel : DomainServiceBase, ISsoChannel
{
    private readonly ChannelConfig? _channel;
    private readonly IReadOnlyList<IEventReplyHandler> _handlers;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务）；
    /// <paramref name="channel"/> 为 null = 集合模板实例（工厂类型索引源，不直接认证——v0.3.0 多通道）。
    /// <paramref name="handlers"/> = IEventReplyHandler 集合（消费方注册，ADR92 集合版守卫工厂帧内供给——
    /// 平台库不注册 handler，防过度设计）；凭证（Token/EncodingAESKey——事件验签信任根）在
    /// <c>AuthenticateAsync</c>/<c>HandleEventAsync</c> 从 cfg.Extra 使用。</summary>
    public WeChatEventChannel(IDomainUser user, IEnumerable<IEventReplyHandler> handlers, ChannelConfig? channel = null) : base(user)
    {
        _channel = channel;
        _handlers = handlers?.ToList() ?? throw new ArgumentNullException(nameof(handlers));
    }

    /// <inheritdoc />
    public string ChannelType => "wechat_event";

    /// <inheritdoc />
    public string ChannelId => _channel?.ChannelId ?? "";

    /// <inheritdoc />
    public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (_channel is null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0));   // 模板实例不可直接认证
        if (!_channel.IsEnabled)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_DISABLED", 0));

        // 平台特定负载（Extra——Token/EncodingAESKey 事件验签信任根）
        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_EVENT_CONFIG_MISSING", 0));

        if (context.Parameters == null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_EVENT_PARAMS_MISSING", 0));

        // 微信官方 URL 参数名 = msg_signature；装配层亦可能透传 "signature"（兼容两种命名）
        if (!context.Parameters.TryGetValue("msg_signature", out var signature))
            context.Parameters.TryGetValue("signature", out signature);
        if (!context.Parameters.TryGetValue("timestamp", out var timestamp)
            || !context.Parameters.TryGetValue("nonce", out var nonce)
            || !context.Parameters.TryGetValue("msg_encrypt", out var msgEncrypt)
            || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_EVENT_PARAMS_MISSING", 0));

        // 信任根一票否决：验签失败 → 拒绝（绝不解密伪造密文）
        if (!WeChatEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_SIGNATURE_INVALID", 0));

        try
        {
            // 安全模式 AES 解密 → FromUserName(openid)
            var xml = WeChatEventCrypto.DecryptMsg(encodingAesKey!, msgEncrypt!);
            var openId = WeChatEventCrypto.ExtractFromUserName(xml);
            return Task.FromResult(new SsoChannelAuthResult(true, openId, null, 2));  // AuthLevel=2
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法——降级失败（信任根仍通过签名；密文损坏返回失败）
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_EVENT_DECRYPT_FAILED", 0));
        }
    }

    /// <inheritdoc />
    public async Task<EventReplyResult?> HandleEventAsync(SsoChannelEventContext context, CancellationToken ct = default)
    {
        // 与 AuthenticateAsync 同款守卫（模板实例/禁用/配置缺失/参数缺失/验签失败/解密失败 → null——
        // 错误语义由装配层 AuthenticateAsync 先行映射（WebExtension 验签成功后才会调本方法）；null 仅表示无平台回复）。
        // ⚠️ 重复验签+解密链（Oracle P2-2 记录——性能非安全，保留设计）：WebExtension 端点已先验签成功，
        // 本方法仍自验——因本方法是通道契约方法，可被直连调用（不经装配层），验签信任根须自持（纵深防御）；
        // 不可用"external_user_id 已存在即跳过验签"优化（存在直调伪造 openid 绕过信任根的漏洞）。
        // Phase 2 契约微调选项（记录，不阻断）：AuthenticateAsync 返回解密 XML 供 HandleEventAsync 复用，需契约微调。
        if (_channel is null)
            return null;
        if (!_channel.IsEnabled)
            return null;

        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey))
            return null;

        if (context.Parameters == null)
            return null;

        // 微信官方 URL 参数名 = msg_signature；装配层亦可能透传 "signature"（兼容两种命名）
        if (!context.Parameters.TryGetValue("msg_signature", out var signature))
            context.Parameters.TryGetValue("signature", out signature);
        if (!context.Parameters.TryGetValue("timestamp", out var timestamp)
            || !context.Parameters.TryGetValue("nonce", out var nonce)
            || !context.Parameters.TryGetValue("msg_encrypt", out var msgEncrypt)
            || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
            return null;

        // 信任根一票否决：验签失败 → 拒绝（绝不解密伪造密文）
        if (!WeChatEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
            return null;

        try
        {
            // 安全模式 AES 解密 → FromUserName(openid) + MsgType/Event（评审 F4）
            var xml = WeChatEventCrypto.DecryptMsg(encodingAesKey!, msgEncrypt!);
            var openId = WeChatEventCrypto.ExtractFromUserName(xml);
            var toUserName = WeChatEventCrypto.ExtractToUserName(xml);
            var (msgType, eventValue) = WeChatEventCrypto.ExtractMsgTypeAndEvent(xml);

            // 富化共享参数字典（评审 F4——MsgType/Event 进 Parameters，供编排/handler 消费；装配层构造
            // Dictionary 实例经 record 传入——原地富化跨调用方可见）
            if (context.Parameters is Dictionary<string, string?> dict)
            {
                dict["external_user_id"] = openId;
                dict["msg_type"] = msgType ?? "";
                dict["event"] = eventValue ?? "";
                dict["to_user_name"] = toUserName;
                dict["from_user_name"] = openId;
            }

            // handler 委托 first-non-null-wins（微信被动回复 = 业务决策，归消费方 IEventReplyHandler——
            // 平台库不注册 handler，防过度设计）：业务内容包微信 XML 被动回复信封（text/xml）
            foreach (var handler in _handlers)
            {
                if (!string.Equals(handler.ChannelType, ChannelType, StringComparison.Ordinal))
                    continue;
                var reply = await handler.HandleAsync(context, ct);
                if (reply is null)
                    continue;

                // 微信 XML 被动回复（text）：ToUserName=原 FromUserName(openid——回复对象)/
                // FromUserName=原 ToUserName(公众号 id)；图文(news)信封归后续迭代
                var xmlReply = BuildTextReplyXml(openId, toUserName, reply.Content);
                return new EventReplyResult(xmlReply, "text/xml");
            }

            // 微信 5 秒超时约束：无业务回复 → 200 空回复（text/xml）——规避超时，异步客服消息兜底归 Phase 2/消费方
            return new EventReplyResult("", "text/xml");
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法——防御性拒（装配层 AuthenticateAsync 已先行映射失败语义）
            return null;
        }
    }

    /// <summary>
    /// 微信 XML 被动回复（text）信封——<c>&lt;xml&gt;&lt;ToUserName&gt;&lt;![CDATA[...]]&gt;&lt;/ToUserName&gt;
    /// &lt;FromUserName&gt;...&lt;/FromUserName&gt;&lt;CreateTime&gt;unix&lt;/CreateTime&gt;
    /// &lt;MsgType&gt;&lt;![CDATA[text]]&gt;&lt;/MsgType&gt;&lt;Content&gt;&lt;![CDATA[...]]&gt;&lt;/Content&gt;&lt;/xml&gt;</c>。
    /// </summary>
    /// <param name="toUserName">回复对象（原 FromUserName = openid）。</param>
    /// <param name="fromUserName">公众号 id（原 ToUserName）。</param>
    /// <param name="content">业务回复文本（handler 内容）。</param>
    private static string BuildTextReplyXml(string toUserName, string fromUserName, string content)
    {
        var createTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"<xml><ToUserName><![CDATA[{toUserName}]]></ToUserName>" +
               $"<FromUserName><![CDATA[{fromUserName}]]></FromUserName>" +
               $"<CreateTime>{createTime}</CreateTime>" +
               $"<MsgType><![CDATA[text]]></MsgType>" +
               $"<Content><![CDATA[{content}]]></Content></xml>";
    }
}