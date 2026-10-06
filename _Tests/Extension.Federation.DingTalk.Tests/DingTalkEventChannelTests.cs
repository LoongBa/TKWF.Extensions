using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using Xunit;

namespace TKWF.Federation.DingTalk.Tests;

/// <summary>
/// <see cref="DingTalkEventChannel"/>（dingtalk_event——钉钉事件订阅推送验签入站）测试——生产路径宿主
/// （真实 DI + <c>AddDingTalkFederationChannels</c> + 探针门面帧内枚举）。
/// <para><b>三道闸门信任根</b>（M3 方案 §3.2/§3.4）：① 合法 signature 通过 / 篡改签名 → 拒
/// （DINGTALK_SIGNATURE_INVALID）；② AES 解密 + <b>receiveid=corpId 校验（第二道闸门——P1-1：receiveid 篡改
/// 样本拒）</b>；③ FromUserId 提取。参数缺失 → 拒；密文损坏 → 解密失败拒。CPU-only 无出站（1200ms 硬约束 P1-2）。</para>
/// </summary>
public class DingTalkEventChannelTests : DingTalkChannelTestHost
{
    private const string TestToken = "test-token";
    private const string TestAppKey = "dingAppKey000000000000";
    private const string TestCorpId = "ding_test_corp";
    private const string TestAesKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"; // 43 字符
    private const string TestOpenId = "oa_scan_openid_1";

    private DingTalkEventChannel Setup()
    {
        ConfigureOptions = o => o.Channels =
        [
            DingTalkTestHost.CreateChannelConfig(appKey: TestAppKey, corpId: TestCorpId, token: TestToken, encodingAESKey: TestAesKey),
        ];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        return Assert.IsType<DingTalkEventChannel>(
            Assert.Single(probe.Channels, c => c.ChannelType == "dingtalk_event"));
    }

    /// <summary>构造合法事件推送样本（msg_encrypt + signature），openId 嵌入 FromUserId、receiveid=corpId。</summary>
    private static (string MsgEncrypt, string Timestamp, string Nonce, string Signature) BuildValidEvent(
        string openId = TestOpenId, string receiveId = TestCorpId)
    {
        var json = $"{{\"EventType\":\"SCAN\",\"FromUserId\":\"{openId}\",\"CorpId\":\"{receiveId}\",\"CreateAt\":1720000000}}";
        var msgEncrypt = DingTalkEventCrypto.EncryptMsg(json, TestAesKey, receiveId);
        var timestamp = "1234567890";
        var nonce = "test-nonce";
        var signature = DingTalkEventCrypto.ComputeSignature(TestToken, timestamp, nonce, msgEncrypt);
        return (msgEncrypt, timestamp, nonce, signature);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSignatureAndCipher_ReturnsFromUserId()
    {
        using var _ = this;
        var channel = Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenId, result.ExternalUserId); // FromUserId = openId（应用维度）
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_SignatureAliasKey_Accepts()
    {
        // 兼容 "msg_signature" 参数名（装配层可能透传该命名）
        using var _ = this;
        var channel = Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenId, result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_TamperedSignature_Rejected_DINGTALK_SIGNATURE_INVALID()
    {
        using var _ = this;
        var channel = Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        // 篡改签名末位——信任根一票否决
        var tampered = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(
                ("signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DINGTALK_SIGNATURE_INVALID", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongLengthSignature_Rejected_NoThrow()
    {
        using var _ = this;
        var channel = Setup();
        var (msgEncrypt, timestamp, nonce, _) = BuildValidEvent();

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(
                ("signature", "short-sig"), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DINGTALK_SIGNATURE_INVALID", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingParams_Rejected_DINGTALK_EVENT_PARAMS_MISSING()
    {
        using var _ = this;
        var channel = Setup();

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(("timestamp", "1234567890")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DINGTALK_EVENT_PARAMS_MISSING", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSignatureTamperedCipher_DecryptFails()
    {
        using var _ = this;
        var channel = Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        // 篡改密文（换首字符）——重签指向篡改密文（验签通过）→ 解密失败（填充/格式损坏）
        var tamperedCipher = (msgEncrypt[0] == 'A' ? 'B' : 'A') + msgEncrypt[1..];
        var signatureForTampered = DingTalkEventCrypto.ComputeSignature(TestToken, timestamp, nonce, tamperedCipher);

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(
                ("signature", signatureForTampered), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", tamperedCipher)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DINGTALK_EVENT_DECRYPT_FAILED", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_ReceiveIdMismatch_Rejected()
    {
        // Oracle 评审 P1-1 第二道闸门——合法密文但 receiveid≠corpId（forged org-change event 防御）：
        // 用错误 corpId 加密 → 重签指向该密文（验签通过）→ 解密 receiveid 校验失败 → 拒
        using var _ = this;
        var channel = Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent(receiveId: "ding_forged_corp");

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DINGTALK_EVENT_DECRYPT_FAILED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }
}
