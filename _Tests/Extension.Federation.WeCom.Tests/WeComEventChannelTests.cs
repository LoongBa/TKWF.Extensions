using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using Xunit;

namespace TKWF.Federation.WeCom.Tests;

/// <summary>
/// <see cref="WeComEventChannel"/>（wecom_event——企业微信通讯录/事件回调验签入站）测试——生产路径宿主
/// （真实 DI + <c>AddWeComFederationChannels</c> + 探针门面帧内枚举）。
/// <para><b>三道闸门信任根</b>（M4 方案 §3.2/§3.4）：① 合法 msg_signature 通过 / 篡改签名 → 拒
/// （WECOM_SIGNATURE_INVALID）；② AES 解密 + <b>ReceiveId 校验（第二道闸门——P1-1：receiveid 篡改样本拒，
/// 防跨企业回调混串）</b>；③ XML 身份提取（UserID/OpenUserID → external_uid 双策略 P0-1）。
/// 参数缺失 → 拒；密文损坏 → 解密失败拒。CPU-only 无出站（回调不阻塞业务）。</para>
/// <para>v0.3.0 多通道：真实实例经 <see cref="ISsoChannelFactory"/> 按 channelId 构造（凭证 Token/EncodingAESKey/CorpId
/// 从 registry Extra 懒加载）。</para>
/// </summary>
public class WeComEventChannelTests : WeComChannelTestHost
{
    private const string TestChannelId = "wecom-main";
    private const string TestToken = "test-token";
    private const string TestCorpId = "ww_test_corp";
    private const string TestAesKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"; // 43 字符
    private const string TestUserId = "usr_123";
    private const string TestOpenUserId = "ou_test_1";

    private async Task<WeComEventChannel> Setup(bool isThirdParty = false)
    {
        ConfigureOptions = o => o.Channels =
        [
            WeComTestHost.CreateChannelConfig(channelId: TestChannelId, corpId: TestCorpId, token: TestToken, encodingAESKey: TestAesKey, isThirdParty: isThirdParty),
        ];
        var (_, user) = BindTestScope();
        // v0.3.0：经 ISsoChannelFactory 按 channelId 构造真实实例（凭证 Token/EncodingAESKey/CorpId 从 registry Extra 懒加载）
        var channel = await user.Use<ISsoChannelFactory>().CreateAsync(TestChannelId, "wecom_event");
        return Assert.IsType<WeComEventChannel>(channel);
    }

    /// <summary>构造合法通讯录回调样本（msg_encrypt + msg_signature），身份嵌入 XML、receiveid=corpId（自建）/suiteId（三方）。</summary>
    private static (string MsgEncrypt, string Timestamp, string Nonce, string Signature) BuildValidEvent(
        string? userId = TestUserId, string? openUserId = null, string receiveId = TestCorpId)
    {
        var xml = $"<xml><ToUserName><![CDATA[{receiveId}]]></ToUserName><FromUserName><![CDATA[sys]]></FromUserName><CreateTime>1403610513</CreateTime><MsgType><![CDATA[event]]></MsgType><Event><![CDATA[change_contact]]></Event><ChangeType><![CDATA[update_user]]></ChangeType>{BuildIdentityXml(userId, openUserId)}</xml>";
        var msgEncrypt = WeComEventCrypto.EncryptMsg(xml, TestAesKey, receiveId);
        var timestamp = "1403610513";
        var nonce = "test-nonce";
        var signature = WeComEventCrypto.ComputeSignature(TestToken, timestamp, nonce, msgEncrypt);
        return (msgEncrypt, timestamp, nonce, signature);
    }

    private static string BuildIdentityXml(string? userId, string? openUserId)
    {
        var xml = "";
        if (userId != null) xml += $"<UserID><![CDATA[{userId}]]></UserID>";
        if (openUserId != null) xml += $"<OpenUserID><![CDATA[{openUserId}]]></OpenUserID>";
        return xml;
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSignatureAndCipher_SelfMember_ReturnsComposite()
    {
        using var _ = this;
        var channel = await Setup(isThirdParty: false);
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent(userId: TestUserId);

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal($"{TestCorpId}:{TestUserId}", result.ExternalUserId); // 自建成员 {CorpId}:{userid} 复合（P0-1）
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSignatureAndCipher_ThirdParty_ReturnsOpenUserId()
    {
        using var _ = this;
        var channel = await Setup(isThirdParty: true);
        // 三方应用——receiveid=suiteId（CorpId 字段承载）+ OpenUserID 优先
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent(userId: TestUserId, openUserId: TestOpenUserId);

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenUserId, result.ExternalUserId); // open_userid 优先（全局唯一，P0-1）
        Assert.Null(result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_SignatureAliasKey_Accepts()
    {
        // 兼容 "signature" 参数名（装配层可能透传该命名）
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent(userId: TestUserId);

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal($"{TestCorpId}:{TestUserId}", result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_TamperedSignature_Rejected_WECOM_SIGNATURE_INVALID()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent(userId: TestUserId);
        // 篡改签名末位——信任根一票否决
        var tampered = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(
                ("msg_signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECOM_SIGNATURE_INVALID", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongLengthSignature_Rejected_NoThrow()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, _) = BuildValidEvent(userId: TestUserId);

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(
                ("msg_signature", "short-sig"), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECOM_SIGNATURE_INVALID", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingParams_Rejected_WECOM_EVENT_PARAMS_MISSING()
    {
        using var _ = this;
        var channel = await Setup();

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(("timestamp", "1403610513")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECOM_EVENT_PARAMS_MISSING", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSignatureTamperedCipher_DecryptFails()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent(userId: TestUserId);
        // 篡改密文（换首字符）——重签指向篡改密文（验签通过）→ 解密失败（填充/格式损坏）
        var tamperedCipher = (msgEncrypt[0] == 'A' ? 'B' : 'A') + msgEncrypt[1..];
        var signatureForTampered = WeComEventCrypto.ComputeSignature(TestToken, timestamp, nonce, tamperedCipher);

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(
                ("msg_signature", signatureForTampered), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", tamperedCipher)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECOM_EVENT_DECRYPT_FAILED", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_ReceiveIdMismatch_Rejected()
    {
        // Oracle 评审 P1-1 第二道闸门——合法密文但 receiveid≠corpid（防跨企业回调混串）：
        // 用错误 corpId 加密 → 重签指向该密文（验签通过）→ 解密 receiveid 校验失败 → 拒
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent(userId: TestUserId, receiveId: "ww_forged_corp");

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECOM_EVENT_DECRYPT_FAILED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    // ═══════════════════ I2-2 契约跟进（V0.4.2——HandleEventAsync/VerifyUrlAsync/Capabilities/P1-3） ═══════════════════

    /// <summary>构造合法事件样本（任意 XML——subscribe/change_contact 等，receiveid=corpId/suiteId）。</summary>
    private static (string MsgEncrypt, string Timestamp, string Nonce, string Signature) BuildEvent(
        string xml, string receiveId = TestCorpId)
    {
        var msgEncrypt = WeComEventCrypto.EncryptMsg(xml, TestAesKey, receiveId);
        var timestamp = "1403610513";
        var nonce = "test-nonce";
        var signature = WeComEventCrypto.ComputeSignature(TestToken, timestamp, nonce, msgEncrypt);
        return (msgEncrypt, timestamp, nonce, signature);
    }

    /// <summary>W-1：HandleEventAsync 正向（change_contact + ChangeType=update_user）——Parsed ChannelEvent：
    /// EventType 复合 change_contact:update_user；ExternalUserId 双策略（自建 {CorpId}:{userid} / 三方 OpenUserID）；
    /// OccurredAt=CreateTime→UTC；RawPayload=明文 XML。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleEventAsync_ValidEvent_ParsesChannelEvent(bool isThirdParty)
    {
        using var _ = this;
        var channel = await Setup(isThirdParty: isThirdParty);
        var (msgEncrypt, timestamp, nonce, signature) = isThirdParty
            ? BuildValidEvent(userId: TestUserId, openUserId: TestOpenUserId)                     // 三方——OpenUserID 优先
            : BuildValidEvent(userId: TestUserId);                                                // 自建——{CorpId}:{userid} 复合

        var outcome = await channel.HandleEventAsync(
            WeComTestHost.CreateEventContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.Parsed, outcome.State);
        var evt = outcome.Event!;
        Assert.Equal(TestChannelId, evt.ChannelId);
        Assert.Equal("wecom_event", evt.ChannelType);
        Assert.Equal(isThirdParty ? TestOpenUserId : $"{TestCorpId}:{TestUserId}", evt.ExternalUserId);
        Assert.Equal("change_contact:update_user", evt.EventType);               // 复合保留子类型（§4.3 建议）
        Assert.Equal("update_user", evt.Source);                                 // Source = ChangeType
        Assert.Equal("change_contact", evt.Attributes["wecom.Event"]);
        Assert.Equal("update_user", evt.Attributes["wecom.ChangeType"]);
        Assert.Equal(TestUserId, evt.Attributes["wecom.UserID"]);
        Assert.Equal(isThirdParty ? TestOpenUserId : null, evt.Attributes["wecom.OpenUserID"]);
        var expected = DateTimeOffset.FromUnixTimeSeconds(1403610513).UtcDateTime;
        Assert.Equal(expected, evt.OccurredAt);                                 // CreateTime → UTC
        Assert.Contains("<Event><![CDATA[change_contact]]></Event>", evt.RawPayload!);
    }

    /// <summary>W-2：HandleEventAsync 篡改签名 → Rejected（一票否决）。</summary>
    [Fact]
    public async Task HandleEventAsync_TamperedSignature_Rejected()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent(userId: TestUserId);
        var tampered = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var outcome = await channel.HandleEventAsync(
            WeComTestHost.CreateEventContext(
                ("msg_signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.Rejected, outcome.State);
    }

    /// <summary>W-3：VerifyUrlAsync echostr 握手正向（WC-2 闭环）——验签 + AES 解密回显明文
    /// （委托 VerifyAndDecryptEcho）；篡改签名 → null（负向同用例 Theory）。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerifyUrlAsync_ValidEcho_EchoesPlain(bool tamperSignature)
    {
        using var _ = this;
        var channel = await Setup();
        var timestamp = "1403610513";
        var nonce = "test-nonce";
        var echostr = WeComEventCrypto.EncryptMsg("echo-plain-123", TestAesKey, TestCorpId);
        var signature = WeComEventCrypto.ComputeSignature(TestToken, timestamp, nonce, echostr);
        if (tamperSignature)
            signature = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var echoPlain = await channel.VerifyUrlAsync(
            WeComTestHost.CreateEventContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("echostr", echostr)),
            CancellationToken.None);

        if (tamperSignature)
            Assert.Null(echoPlain);
        else
            Assert.Equal("echo-plain-123", echoPlain);                            // 回显解密明文
    }

    /// <summary>W-4：VerifyUrlAsync 失败归一 null（缺参数/验签失败/解密失败——装配层 400 语义）。</summary>
    [Fact]
    public async Task VerifyUrlAsync_Failures_Null()
    {
        using var _ = this;
        var channel = await Setup();
        var timestamp = "1403610513";
        var nonce = "test-nonce";

        // ① 缺参（无 echostr）→ null
        var missing = await channel.VerifyUrlAsync(
            WeComTestHost.CreateEventContext(("timestamp", timestamp), ("nonce", nonce)), CancellationToken.None);
        Assert.Null(missing);

        // ② 验签失败（msg_signature 短签名）→ null
        var badSig = await channel.VerifyUrlAsync(
            WeComTestHost.CreateEventContext(
                ("msg_signature", "short-sig"), ("timestamp", timestamp), ("nonce", nonce), ("echostr", "cipher-x")),
            CancellationToken.None);
        Assert.Null(badSig);

        // ③ 解密失败（echostr 用错误 AESKey 加密——签名指向该密文通过 → DecryptAndVerify 抛 → catch 归一 null）
        var echoWrongKey = WeComEventCrypto.EncryptMsg("echo-plain-123", WeComTestHost.GenerateEncodingAESKey(), TestCorpId);
        var sigWrong = WeComEventCrypto.ComputeSignature(TestToken, timestamp, nonce, echoWrongKey);
        var decryptFail = await channel.VerifyUrlAsync(
            WeComTestHost.CreateEventContext(
                ("msg_signature", sigWrong), ("timestamp", timestamp), ("nonce", nonce), ("echostr", echoWrongKey)),
            CancellationToken.None);
        Assert.Null(decryptFail);
    }

    /// <summary>W-5：HandleEventAsync subscribe 事件原文透传（P1-3 定案选项 b）——EventType="subscribe"
    /// 不产 follow、不翻关注状态；身份 = FromUserName 兜底（关注事件无 UserID/OpenUserID，§4.3 三源链）。</summary>
    [Fact]
    public async Task HandleEventAsync_SubscribeEvent_Passthrough()
    {
        using var _ = this;
        var channel = await Setup();
        var xml = "<xml><ToUserName><![CDATA[ww_test_corp]]></ToUserName><FromUserName><![CDATA[om_member_openid]]></FromUserName>"
            + "<CreateTime>1403610513</CreateTime><MsgType><![CDATA[event]]></MsgType><Event><![CDATA[subscribe]]></Event></xml>";
        var (msgEncrypt, timestamp, nonce, signature) = BuildEvent(xml);

        var outcome = await channel.HandleEventAsync(
            WeComTestHost.CreateEventContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.Parsed, outcome.State);
        var evt = outcome.Event!;
        Assert.Equal("subscribe", evt.EventType);                               // 原文透传——不产 follow（P1-3）
        Assert.Equal("om_member_openid", evt.ExternalUserId);                   // FromUserName 兜底（三源链末位）
        Assert.Equal("subscribe", evt.Attributes["wecom.Event"]);
    }

    /// <summary>W-6：WeComChannelSource.Capabilities 含 Event|OAuth（能力面锁定——I2-2 §3.5，不声明 Follow）。</summary>
    [Fact]
    public async Task ChannelSource_Capabilities_DeclaresEventOAuth()
    {
        using var _ = this;
        await Setup();
        var (provider, _) = BindTestScope();
        var source = provider.GetRequiredService<IChannelSource>();
        var caps = source.Capabilities;
        Assert.True(caps.HasFlag(ChannelCapabilities.Event));
        Assert.True(caps.HasFlag(ChannelCapabilities.OAuth));
        Assert.False(caps.HasFlag(ChannelCapabilities.Follow));                 // 不声明 Follow（§3.5——企微无公众号关注订阅语义）
    }
}
