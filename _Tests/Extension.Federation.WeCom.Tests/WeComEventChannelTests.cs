using System.Threading;
using System.Threading.Tasks;
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
}
