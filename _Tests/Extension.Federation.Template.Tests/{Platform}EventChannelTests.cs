using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using Xunit;

namespace TKWF.Federation.{Namespace}.Tests;

/// <summary>
/// <see cref="{Platform}EventChannel"/>（{platform}_event——平台事件推送验签入站）测试——生产路径宿主
/// （真实 DI + <c>Add{Platform}FederationChannels</c> + 探针门面帧内枚举）。
/// <para><b>信任根一票否决</b>：合法 msg_signature 通过；篡改签名 → 拒（{Namespace}_SIGNATURE_INVALID）；
/// 参数缺失 → 拒；密文损坏 → 解密失败拒。</para>
/// ⚠️ 仅**有入站回调平台**（微信/钉钉/企业微信）；出站-only 平台删除本文件。
/// </summary>
public class {Platform}EventChannelTests : {Platform}ChannelTestHost
{
    private const string TestToken = "test-token";
    private const string TestAppId = "test-appid";
    private const string TestAesKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"; // 43 字符
    private const string TestExternalUserId = "test_external_uid_event_1";

    private {Platform}EventChannel Setup()
    {
        ConfigureOptions = o => o.Channels =
        [
            {Platform}TestHost.CreateChannelConfig(appId: TestAppId, token: TestToken, encodingAESKey: TestAesKey),
        ];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        return Assert.IsType<{Platform}EventChannel>(
            Assert.Single(probe.Channels, c => c.ChannelType == "{platform}_event"));
    }

    /// <summary>构造合法事件推送样本（msg_encrypt + msg_signature），external_uid 嵌入 FromUserName。</summary>
    private static (string MsgEncrypt, string Timestamp, string Nonce, string Signature) BuildValidEvent(string externalUserId = TestExternalUserId)
    {
        var xml = $"<xml><ToUserName><![CDATA[gh_test]]></ToUserName><FromUserName><![CDATA[{externalUserId}]]></FromUserName><CreateTime>1234567890</CreateTime><MsgType><![CDATA[event]]></MsgType><Event><![CDATA[CLICK]]></Event></xml>";
        var msgEncrypt = {Platform}EventCrypto.EncryptMsg(xml, TestAesKey, TestAppId);
        var timestamp = "1234567890";
        var nonce = "test-nonce";
        var signature = {Platform}EventCrypto.ComputeSignature(TestToken, timestamp, nonce, msgEncrypt);
        return (msgEncrypt, timestamp, nonce, signature);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSignatureAndCipher_ReturnsExternalId()
    {
        using var _ = this;
        var channel = Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();

        var result = await channel.AuthenticateAsync(
            {Platform}TestHost.CreateContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestExternalUserId, result.ExternalUserId); // FromUserName = external_uid
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_SignatureAliasKey_Accepts()
    {
        // 兼容 "signature" 参数名（装配层可能透传该命名）
        using var _ = this;
        var channel = Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();

        var result = await channel.AuthenticateAsync(
            {Platform}TestHost.CreateContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestExternalUserId, result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_TamperedSignature_Rejected_{Namespace}_SIGNATURE_INVALID()
    {
        using var _ = this;
        var channel = Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        // 篡改签名末位——信任根一票否决
        var tampered = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var result = await channel.AuthenticateAsync(
            {Platform}TestHost.CreateContext(
                ("msg_signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("{Namespace}_SIGNATURE_INVALID", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongLengthSignature_Rejected_NoThrow()
    {
        using var _ = this;
        var channel = Setup();
        var (msgEncrypt, timestamp, nonce, _) = BuildValidEvent();

        var result = await channel.AuthenticateAsync(
            {Platform}TestHost.CreateContext(
                ("msg_signature", "short-sig"), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("{Namespace}_SIGNATURE_INVALID", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingParams_Rejected_{Namespace}_EVENT_PARAMS_MISSING()
    {
        using var _ = this;
        var channel = Setup();

        var result = await channel.AuthenticateAsync(
            {Platform}TestHost.CreateContext(("timestamp", "1234567890")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("{Namespace}_EVENT_PARAMS_MISSING", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSignatureTamperedCipher_DecryptFails()
    {
        using var _ = this;
        var channel = Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        // 篡改密文（换首字符）——重签指向篡改密文（验签通过）→ 解密失败（填充/格式损坏）
        var tamperedCipher = (msgEncrypt[0] == 'A' ? 'B' : 'A') + msgEncrypt[1..];
        var signatureForTampered = {Platform}EventCrypto.ComputeSignature(TestToken, timestamp, nonce, tamperedCipher);

        var result = await channel.AuthenticateAsync(
            {Platform}TestHost.CreateContext(
                ("msg_signature", signatureForTampered), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", tamperedCipher)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("{Namespace}_EVENT_DECRYPT_FAILED", result.FailReason);
    }
}
