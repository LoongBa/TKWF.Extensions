using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.WeCom;

/// <summary>
/// 企业微信通讯录/事件回调通道（`ISsoChannel: wecom_event`）——组织变更/事件推送入站通知。
/// <para>流程（M4 方案 §3.2）：企业微信通讯录变更/事件推送 → 装配层接收端点（映射入站）→ 本通道
/// <see cref="AuthenticateAsync"/> 执行<b>三道闸门</b>：① <see cref="WeComEventCrypto.VerifySignature"/>
/// 验签（信任根一票否决）；② <see cref="WeComEventCrypto.DecryptAndVerify"/> AES 解密并<b>校验 ReceiveId
/// （自建 corpid / 三方 suiteid——Oracle 评审 P1-1：防跨企业回调混串）</b>；③
/// <see cref="WeComEventCrypto.ExtractFromXml"/> 提取 <c>UserID</c>/<c>OpenUserID</c> → external_uid 双策略
/// （三方 <c>open_userid</c> / 自建 <c>{CorpId}:{userid}</c> 复合——Oracle 评审 P0-1）→
/// 返回 <see cref="SsoChannelAuthResult"/>。回调只做增量/解绑通知——官方"无法保证 100% 回调成功"→
/// 业务不强依赖回调（认证主路径走 OAuth 出站）。</para>
/// <para>多通道联邦（v0.3.0）：channel 由 <see cref="ChannelConfig"/>（工厂预取传入）——<c>null</c> = 集合模板实例
/// （不直接认证）。凭证（CorpId/Token/EncodingAESKey）从 <see cref="ChannelConfig.Extra"/> 使用
/// （M7：入站信任根负载进 Extra，不挤占公共列）。</para>
/// <para>边界：<b>URL 验证握手（echostr 回显，1s 硬约束）归装配层</b>——库提供 <see cref="WeComEventCrypto.VerifyAndDecryptEcho"/>
/// 原语（Oracle 评审 P1-6），不在本通道 <c>AuthenticateAsync</c> 内（后者服务事件回调业务）；回调响应策略
/// 统一（验签+解密+落库归阻塞路径、业务异步、按平台最严超时——P2-2 回填 N1 模板）。</para>
/// <para>Context 参数（装配层接收端点传入——推送 URL query + body）：<c>Parameters["msg_signature"]</c>（企业微信
/// 官方参数名；兼容 "signature" 别名）/<c>["timestamp"]</c>/<c>["nonce"]</c>/<c>["msg_encrypt"]</c>。</para>
/// <para>注册：库扩展方法 <c>AddWeComFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92）；
/// 集合元素 = 模板实例（channel=null），真实实例由工厂帧内 ActivatorUtilities 构造。</para>
/// </summary>
[DiContractIgnore]
public sealed class WeComEventChannel : DomainServiceBase, ISsoChannel
{
    private readonly ChannelConfig? _channel;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务）；
    /// <paramref name="channel"/> 为 null = 集合模板实例（工厂类型索引源，不直接认证——v0.3.0 多通道）。
    /// 凭证（Token/EncodingAESKey/CorpId——三道闸门信任根 + receiveid 期望）在 <c>AuthenticateAsync</c> 从 cfg.Extra 使用。</summary>
    public WeComEventChannel(IDomainUser user, ChannelConfig? channel = null) : base(user)
    {
        _channel = channel;
    }

    /// <inheritdoc />
    public string ChannelType => "wecom_event";

    /// <inheritdoc />
    public string ChannelId => _channel?.ChannelId ?? "";

    /// <inheritdoc />
    public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (_channel is null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0));   // 模板实例不可直接认证
        if (!_channel.IsEnabled)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_DISABLED", 0));

        // 平台特定负载（Extra——CorpId/Token/EncodingAESKey 三道闸门信任根 + receiveid 期望）
        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        _channel.Extra.TryGetValue("CorpId", out var corpId);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey) || string.IsNullOrEmpty(corpId))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_EVENT_CONFIG_MISSING", 0));

        if (context.Parameters == null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_EVENT_PARAMS_MISSING", 0));

        // 企业微信官方 URL 参数名 = msg_signature；装配层亦可能透传 "signature"（兼容两种命名）
        if (!context.Parameters.TryGetValue("msg_signature", out var signature))
            context.Parameters.TryGetValue("signature", out signature);
        if (!context.Parameters.TryGetValue("timestamp", out var timestamp)
            || !context.Parameters.TryGetValue("nonce", out var nonce)
            || !context.Parameters.TryGetValue("msg_encrypt", out var msgEncrypt)
            || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_EVENT_PARAMS_MISSING", 0));

        // 信任根一票否决：验签失败 → 拒绝（绝不解密伪造密文）
        if (!WeComEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_SIGNATURE_INVALID", 0));

        try
        {
            // AES 解密 + ReceiveId 校验（第二道闸门——自建 corpid / 三方 suiteid，不符抛）→ 事件体 XML → 身份提取
            // ⚠️ CPU-only 无出站调用（回调不阻塞业务——验签+解密+落库归阻塞路径，业务异步）
            var xml = WeComEventCrypto.DecryptAndVerify(encodingAesKey!, msgEncrypt!, corpId!);
            var (userId, openUserId) = WeComEventCrypto.ExtractFromXml(xml);

            // external_uid 双策略（Oracle 评审 P0-1）——三方 open_userid（全局唯一）/ 自建 {CorpId}:{userid} 复合
            string? externalUserId;
            if (!string.IsNullOrEmpty(openUserId))
                externalUserId = openUserId;
            else if (!string.IsNullOrEmpty(userId))
                externalUserId = $"{corpId}:{userId}";
            else
                return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_EVENT_IDENTITY_MISSING", 0));

            return Task.FromResult(new SsoChannelAuthResult(true, externalUserId, null, 2));  // AuthLevel=2
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法/receiveid 不匹配——降级失败（信任根仍通过签名；密文损坏/企业不符返回失败）
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_EVENT_DECRYPT_FAILED", 0));
        }
    }
}
