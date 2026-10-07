using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.DingTalk;

/// <summary>
/// 钉钉事件订阅推送通道（`ISsoChannel: dingtalk_event`）——扫码成功/组织变更等事件身份获取。
/// <para>流程（M3 方案 §3.2）：钉钉事件订阅推送 → 装配层接收端点（映射入站）→ 本通道
/// <see cref="AuthenticateAsync"/> 执行<b>三道闸门</b>：① <see cref="DingTalkEventCrypto.VerifySignature"/>
/// 验签（信任根一票否决）；② <see cref="DingTalkEventCrypto.DecryptMsg"/> AES 解密并<b>校验 receiveid=corpId
/// （第二道闸门——Oracle 评审 P1-1：forged org-change event 可授予企业访问权）</b>；③
/// <see cref="DingTalkEventCrypto.ExtractFromUserId"/> 提取事件体 JSON <c>FromUserId</c>（= openId，应用维度
/// ——Oracle 评审 P1-6）→ 返回 <see cref="SsoChannelAuthResult"/>（ExternalUserId=FromUserId）。</para>
/// <para><b>1200ms 硬约束（Oracle 评审 P1-2）</b>：本通道 <c>AuthenticateAsync</c> = 验签 + 解密 +
/// receiveid 校验 + FromUserId 提取（<b>CPU-only，无出站调用，&lt;50ms</b>）；落库即返 success 是装配层职责
/// （persist-then-ack 模式 + IHostedService/Channel&lt;T&gt; 队列推荐）——1200ms 预算覆盖装配层全链。</para>
/// <para>Context 参数（装配层接收端点传入——钉钉推送 URL query + body）：<c>Parameters["signature"]</c>（钉钉官方
/// 主参数名；兼容 "msg_signature" 别名）/<c>["timestamp"]</c>/<c>["nonce"]</c>/<c>["msg_encrypt"]</c>。</para>
/// <para>边界：本通道只管"验签+解密+receiveid 校验+取 FromUserId"，不负责事件业务处理/异步编排（归装配层）。</para>
/// <para>注册：库扩展方法 <c>AddDingTalkFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92）；
/// channel 业务参数（CorpId/Token/EncodingAESKey）经工厂预取的 <see cref="ChannelConfig"/> 传入
/// （多通道联邦 v0.3.0——不占 ctor IDomainUser 槽）。</para>
/// </summary>
[DiContractIgnore]
public sealed class DingTalkEventChannel : DomainServiceBase, ISsoChannel
{
    private readonly ChannelConfig? _channel;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务）；
    /// <paramref name="channel"/> 为 null = 集合模板实例（工厂类型索引源，不直接认证——v0.3.0 多通道）。
    /// 凭证（CorpId/Token/EncodingAESKey——事件验签三道闸门信任根）在 <c>AuthenticateAsync</c> 从 cfg.Extra 使用。</summary>
    public DingTalkEventChannel(IDomainUser user, ChannelConfig? channel = null) : base(user)
    {
        _channel = channel;
    }

    /// <inheritdoc />
    public string ChannelType => "dingtalk_event";

    /// <inheritdoc />
    public string ChannelId => _channel?.ChannelId ?? "";

    /// <inheritdoc />
    public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (_channel is null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0));   // 模板实例不可直接认证
        if (!_channel.IsEnabled)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_DISABLED", 0));

        // 平台特定负载（Extra——CorpId/Token/EncodingAESKey 事件验签三道闸门信任根）
        _channel.Extra.TryGetValue("CorpId", out var corpId);
        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        if (string.IsNullOrEmpty(corpId) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "DINGTALK_EVENT_CONFIG_MISSING", 0));

        if (context.Parameters == null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "DINGTALK_EVENT_PARAMS_MISSING", 0));

        // 钉钉官方 URL 参数名 = signature；装配层亦可能透传 "msg_signature"（兼容两种命名）
        if (!context.Parameters.TryGetValue("signature", out var signature))
            context.Parameters.TryGetValue("msg_signature", out signature);
        if (!context.Parameters.TryGetValue("timestamp", out var timestamp)
            || !context.Parameters.TryGetValue("nonce", out var nonce)
            || !context.Parameters.TryGetValue("msg_encrypt", out var msgEncrypt)
            || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "DINGTALK_EVENT_PARAMS_MISSING", 0));

        // 信任根一票否决：验签失败 → 拒绝（绝不解密伪造密文）
        if (!DingTalkEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "DINGTALK_SIGNATURE_INVALID", 0));

        try
        {
            // 安全模式 AES 解密 + receiveid 校验（第二道闸门——corpId 不匹配抛）→ 事件体 JSON → FromUserId(openId)
            // ⚠️ CPU-only 无出站调用（1200ms 硬约束 P1-2——库侧 <50ms，落库归装配层）
            var json = DingTalkEventCrypto.DecryptMsg(encodingAesKey!, msgEncrypt!, corpId!);
            var fromUserId = DingTalkEventCrypto.ExtractFromUserId(json);
            return Task.FromResult(new SsoChannelAuthResult(true, fromUserId, null, 2));  // AuthLevel=2 对齐微信便捷语义
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法/receiveid 不匹配——降级失败（信任根仍通过签名；密文损坏/org 不符返回失败）
            return Task.FromResult(new SsoChannelAuthResult(false, null, "DINGTALK_EVENT_DECRYPT_FAILED", 0));
        }
    }
}
