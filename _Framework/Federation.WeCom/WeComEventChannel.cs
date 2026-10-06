using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

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
/// <para>边界：<b>URL 验证握手（echostr 回显，1s 硬约束）归装配层</b>——库提供 <see cref="WeComEventCrypto.VerifyAndDecryptEcho"/>
/// 原语（Oracle 评审 P1-6），不在本通道 <c>AuthenticateAsync</c> 内（后者服务事件回调业务）；回调响应策略
/// 统一（验签+解密+落库归阻塞路径、业务异步、按平台最严超时——P2-2 回填 N1 模板）。</para>
/// <para>Context 参数（装配层接收端点传入——推送 URL query + body）：<c>Parameters["msg_signature"]</c>（企业微信
/// 官方参数名；兼容 "signature" 别名）/<c>["timestamp"]</c>/<c>["nonce"]</c>/<c>["msg_encrypt"]</c>。</para>
/// <para>注册：库扩展方法 <c>AddWeComFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92）；
/// channel 业务参数（CorpId/Token/EncodingAESKey）经 Options 注入不占 ctor IDomainUser 槽。</para>
/// </summary>
[DiContractIgnore]
public sealed class WeComEventChannel : DomainServiceBase, ISsoChannel
{
    private readonly WeComChannelConfig _channel;

    /// <summary>构造——channel 由配置选区（Channels 首项默认；多实例归后续迭代）。</summary>
    public WeComEventChannel(IDomainUser user, IOptions<WeComOptions> options) : base(user)
    {
        _channel = options?.Value.Channels.FirstOrDefault()
            ?? throw new InvalidOperationException("企业微信事件 channel 未配置：TKWF:Federation:WeCom 节 Channels 为空");
    }

    /// <inheritdoc />
    public string ChannelType => "wecom_event";

    /// <inheritdoc />
    public string ChannelId => _channel.ChannelId;

    /// <inheritdoc />
    public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
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
        if (!WeComEventCrypto.VerifySignature(_channel.Token, timestamp!, nonce!, msgEncrypt!, signature!))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_SIGNATURE_INVALID", 0));

        try
        {
            // AES 解密 + ReceiveId 校验（第二道闸门——自建 corpid / 三方 suiteid，不符抛）→ 事件体 XML → 身份提取
            // ⚠️ CPU-only 无出站调用（回调不阻塞业务——验签+解密+落库归阻塞路径，业务异步）
            var xml = WeComEventCrypto.DecryptAndVerify(_channel.EncodingAESKey, msgEncrypt!, _channel.CorpId);
            var (userId, openUserId) = WeComEventCrypto.ExtractFromXml(xml);

            // external_uid 双策略（Oracle 评审 P0-1）——三方 open_userid（全局唯一）/ 自建 {CorpId}:{userid} 复合
            string? externalUserId;
            if (!string.IsNullOrEmpty(openUserId))
                externalUserId = openUserId;
            else if (!string.IsNullOrEmpty(userId))
                externalUserId = $"{_channel.CorpId}:{userId}";
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
