using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.{Namespace};

/// <summary>
/// {平台}事件推送通道（`ISsoChannel: {platform}_event`）——平台事件推送身份获取（**有入站回调平台**）。
/// <para>流程：用户触发事件 → 平台推送 → 装配层接收端点（验签 + 解密的 <b>信任根</b> 由本通道执行）→
/// <c>FromUserName</c>=external_uid → 归一 <c>(channel_id, external_uid) → uid</c> → 编排层转 accesscode。</para>
/// <para><b>信任根一票否决</b>：<see cref="{Platform}EventCrypto.VerifySignature"/> 验签失败即拒
/// （FailReason={PlatformName}_SIGNATURE_INVALID），绝不解密伪造密文。</para>
/// <para>Context 参数（装配层接收端点传入——平台推送 URL query + body）：</para>
/// <para>① 验签：<c>Parameters["signature"]</c>/<c>["timestamp"]</c>/<c>["nonce"]</c>/<c>["msg_encrypt"]</c>
/// （平台参数名差异收敛于此——WeChat 先例兼容 <c>msg_signature</c>/<c>signature</c> 双命名）；
/// ② 解密后 FromUserName 即 external_uid。</para>
/// <para>边界：本通道只管"验签+解密+取 external_uid"，不负责事件业务处理/被动回复（归装配层/编排层）。</para>
/// <para>注册：库扩展方法 <c>Add{Platform}FederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92）；
/// channel 业务参数经 Options 注入不占 ctor IDomainUser 槽。</para>
/// </summary>
[DiContractIgnore]
public sealed class {Platform}EventChannel : DomainServiceBase, ISsoChannel
{
    private readonly ChannelConfig? _channel;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入；多通道联邦 v0.3.0）；
    /// null = 集合模板实例（工厂类型索引源，不直接认证）。凭证（Token/EncodingAESKey）经 Extra 注入。</summary>
    public {Platform}EventChannel(IDomainUser user, ChannelConfig? channel = null) : base(user)
    {
        _channel = channel;
    }

    /// <inheritdoc />
    public string ChannelType => "{platform}_event";

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
            return Task.FromResult(new SsoChannelAuthResult(false, null, $"{PlatformName}_EVENT_CONFIG_MISSING", 0));

        if (context.Parameters == null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, $"{PlatformName}_EVENT_PARAMS_MISSING", 0));

        // ⚠️ 平台差异：验签参数名（WeChat msg_signature/signature 双命名兼容；平台特有命名在此收敛）
        if (!context.Parameters.TryGetValue("msg_signature", out var signature))
            context.Parameters.TryGetValue("signature", out signature);
        if (!context.Parameters.TryGetValue("timestamp", out var timestamp)
            || !context.Parameters.TryGetValue("nonce", out var nonce)
            || !context.Parameters.TryGetValue("msg_encrypt", out var msgEncrypt)
            || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
            return Task.FromResult(new SsoChannelAuthResult(false, null, $"{PlatformName}_EVENT_PARAMS_MISSING", 0));

        // 信任根一票否决：验签失败 → 拒绝（绝不解密伪造密文）
        if (!{Platform}EventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
            return Task.FromResult(new SsoChannelAuthResult(false, null, $"{PlatformName}_SIGNATURE_INVALID", 0));

        try
        {
            // 解密 → FromUserName(external_uid)
            var xml = {Platform}EventCrypto.DecryptMsg(encodingAesKey!, msgEncrypt!);
            var externalUserId = {Platform}EventCrypto.ExtractFromUserName(xml);
            return Task.FromResult(new SsoChannelAuthResult(true, externalUserId, null, 2));  // AuthLevel=2
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法——降级失败（信任根仍通过签名；密文损坏返回失败）
            return Task.FromResult(new SsoChannelAuthResult(false, null, $"{PlatformName}_EVENT_DECRYPT_FAILED", 0));
        }
    }

    private static string PlatformName => "{Namespace}";
}
