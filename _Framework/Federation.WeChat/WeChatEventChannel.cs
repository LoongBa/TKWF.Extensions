using System;
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
/// </summary>
[DiContractIgnore]
public sealed class WeChatEventChannel : DomainServiceBase, ISsoChannel
{
    private readonly ChannelConfig? _channel;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务）；
    /// <paramref name="channel"/> 为 null = 集合模板实例（工厂类型索引源，不直接认证——v0.3.0 多通道）。
    /// 凭证（Token/EncodingAESKey——事件验签信任根）在 <c>AuthenticateAsync</c> 从 cfg.Extra 使用。</summary>
    public WeChatEventChannel(IDomainUser user, ChannelConfig? channel = null) : base(user)
    {
        _channel = channel;
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
}