using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
    private const string TestChannelId = "dingtalk-event";

    private async Task<DingTalkEventChannel> Setup()
    {
        ConfigureOptions = o => o.Channels =
        [
            DingTalkTestHost.CreateChannelConfig(channelId: TestChannelId, appKey: TestAppKey, corpId: TestCorpId, token: TestToken, encodingAESKey: TestAesKey),
        ];
        var (_, user) = BindTestScope();
        // v0.3.0：经 ISsoChannelFactory 按 channelId 构造真实实例（凭证 CorpId/Token/EncodingAESKey 从 registry Extra 懒加载）
        var channel = await user.Use<ISsoChannelFactory>().CreateAsync(TestChannelId, "dingtalk_event");
        return Assert.IsType<DingTalkEventChannel>(channel);
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
        var channel = await Setup();
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
        var channel = await Setup();
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
        var channel = await Setup();
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
        var channel = await Setup();
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
        var channel = await Setup();

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(("timestamp", "1234567890")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DINGTALK_EVENT_PARAMS_MISSING", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSignatureTamperedCipher_DecryptFails()
    {
        using var _ = this;
        var channel = await Setup();
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
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent(receiveId: "ding_forged_corp");

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DINGTALK_EVENT_DECRYPT_FAILED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    // ═══════════════════ I2-2 契约跟进（V0.4.2——HandleEventAsync/VerifyUrlAsync/Capabilities/P0-1/P1-2） ═══════════════════

    /// <summary>构造无 FromUserId 事件样本（check_url / user_add_org 等组织事件——P0-1/P1-2 判据共用）。</summary>
    private static (string MsgEncrypt, string Timestamp, string Nonce, string Signature) BuildIdentityLessEvent(
        string eventType, string receiveId = TestCorpId)
    {
        var json = $"{{\"EventType\":\"{eventType}\",\"CorpId\":\"{receiveId}\"}}";
        var msgEncrypt = DingTalkEventCrypto.EncryptMsg(json, TestAesKey, receiveId);
        var timestamp = "1234567890";
        var nonce = "test-nonce";
        var signature = DingTalkEventCrypto.ComputeSignature(TestToken, timestamp, nonce, msgEncrypt);
        return (msgEncrypt, timestamp, nonce, signature);
    }

    /// <summary>D-1：HandleEventAsync 正向（密文参数名 encrypt 官方主键）——SCAN 事件 → Parsed ChannelEvent：
    /// EventType=scan（归一）；ExternalUserId=FromUserId；OccurredAt=CreateAt→UTC 精确秒；dingtalk. 前缀 Attributes；
    /// RawPayload=明文 JSON。</summary>
    [Fact]
    public async Task HandleEventAsync_ValidSignatureAndCipher_ParsesChannelEvent()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        var json = $"{{\"EventType\":\"SCAN\",\"FromUserId\":\"{TestOpenId}\",\"CorpId\":\"{TestCorpId}\",\"CreateAt\":1720000000}}";

        var outcome = await channel.HandleEventAsync(
            DingTalkTestHost.CreateEventContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.Parsed, outcome.State);
        var evt = outcome.Event!;
        Assert.Equal(TestChannelId, evt.ChannelId);
        Assert.Equal("dingtalk_event", evt.ChannelType);
        Assert.Equal(TestOpenId, evt.ExternalUserId);
        Assert.Equal("scan", evt.EventType);                                  // SCAN → scan（§4.1 归一）
        Assert.Null(evt.EventKey);
        Assert.Equal(TestCorpId, evt.Source);                                 // Source = 事件体 CorpId
        Assert.Null(evt.MessageId);
        var expected = DateTimeOffset.FromUnixTimeSeconds(1720000000).UtcDateTime;
        Assert.Equal(expected, evt.OccurredAt);                               // CreateAt → UTC 精确秒
        Assert.Equal(json, evt.RawPayload);                                   // RawPayload = 明文 JSON
        Assert.Equal("SCAN", evt.Attributes["dingtalk.EventType"]);           // 原文透传
        Assert.Equal(TestOpenId, evt.Attributes["dingtalk.FromUserId"]);
        Assert.Equal(TestCorpId, evt.Attributes["dingtalk.CorpId"]);
        Assert.Equal("1720000000", evt.Attributes["dingtalk.CreateAt"]);
    }

    /// <summary>D-2：HandleEventAsync 篡改签名 → Rejected（一票否决，绝不解密）。</summary>
    [Fact]
    public async Task HandleEventAsync_TamperedSignature_Rejected()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        var tampered = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var outcome = await channel.HandleEventAsync(
            DingTalkTestHost.CreateEventContext(
                ("signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.Rejected, outcome.State);
    }

    /// <summary>D-3：HandleEventAsync 无 FromUserId（check_url / user_add_org 组织事件）→ NotApplicable
    /// （不造半个事件——契约 L12-13；§3.0 守卫统一判据；P0-1/P1-2 同判）。</summary>
    [Theory]
    [InlineData("check_url")]
    [InlineData("user_add_org")]
    public async Task HandleEventAsync_MissingFromUserId_NotApplicable(string eventType)
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildIdentityLessEvent(eventType);

        var outcome = await channel.HandleEventAsync(
            DingTalkTestHost.CreateEventContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.NotApplicable, outcome.State);
        Assert.Null(outcome.Event);
    }

    /// <summary>D-4：VerifyUrlAsync 正向（P0-2 加密应答信封断言重写）——check_url 与业务事件（SCAN）同型：
    /// 返回加密信封 JSON 四字段（encrypt/msg_signature/timeStamp/nonce）；encrypt 经 DecryptMsg 解密 = "success"；
    /// msg_signature 与 ComputeSignature 对拍；非明文 success。</summary>
    [Theory]
    [InlineData("check_url")]
    [InlineData("SCAN")]
    public async Task VerifyUrlAsync_ValidInbound_ReturnsEncryptedEnvelope(string eventType)
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = eventType == "check_url"
            ? BuildIdentityLessEvent(eventType)
            : BuildValidEvent();

        var envelope = await channel.VerifyUrlAsync(
            DingTalkTestHost.CreateEventContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.NotNull(envelope);
        Assert.NotEqual("success", envelope);                                 // 非明文 success（P0-2）
        using var doc = System.Text.Json.JsonDocument.Parse(envelope!);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        var encrypt = root.GetProperty("encrypt").GetString()!;
        var msgSignature = root.GetProperty("msg_signature").GetString()!;
        var timeStamp = root.GetProperty("timeStamp").GetString()!;
        var nonceOut = root.GetProperty("nonce").GetString()!;
        Assert.Equal(timestamp, timeStamp);                                   // 信封回显本请求 timestamp
        Assert.Equal(nonce, nonceOut);                                        // 信封回显本请求 nonce
        Assert.Equal("success", DingTalkEventCrypto.DecryptMsg(TestAesKey, encrypt, TestCorpId));
        Assert.Equal(DingTalkEventCrypto.ComputeSignature(TestToken, timeStamp, nonceOut, encrypt), msgSignature);
    }

    /// <summary>D-5：VerifyUrlAsync 篡改签名 → null（装配层 400 语义）。</summary>
    [Fact]
    public async Task VerifyUrlAsync_TamperedSignature_Null()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildIdentityLessEvent("check_url");
        var tampered = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var envelope = await channel.VerifyUrlAsync(
            DingTalkTestHost.CreateEventContext(
                ("signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.Null(envelope);
    }

    /// <summary>D-6：DingTalkChannelSource.Capabilities 含 Event|OAuth（能力面锁定——I2-2 §3.5）。</summary>
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
        Assert.False(caps.HasFlag(ChannelCapabilities.Follow));                // 不声明 Follow（§3.5）
    }

    /// <summary>D-7：HandleEventAsync 参数缺失（query 与 __raw 均无密文且 __raw 非 JSON）→ Rejected（兜底提取负路径）。</summary>
    [Fact]
    public async Task HandleEventAsync_MissingCipher_ParamsRejected()
    {
        using var _ = this;
        var channel = await Setup();

        var outcome = await channel.HandleEventAsync(
            DingTalkTestHost.CreateEventContext(
                ("signature", "sig-1"), ("timestamp", "1234567890"), ("nonce", "n-1"), ("__raw", "not-json-body")),
            CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.Rejected, outcome.State);
    }

    /// <summary>D-8：密文参数名兼容（P0-1 双键双位实证）——body JSON 单键 {"encrypt":"..."} 与
    /// {"msg_encrypt":"..."} 别名样本均 → Parsed（query 无密文 → body JSON 兜底提取）。</summary>
    [Theory]
    [InlineData("encrypt")]
    [InlineData("msg_encrypt")]
    public async Task HandleEventAsync_EncryptParamName_Compatibility(string bodyKey)
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        var body = $"{{\"{bodyKey}\":\"{msgEncrypt}\"}}";

        var outcome = await channel.HandleEventAsync(
            DingTalkTestHost.CreateEventContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("__raw", body)),
            CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.Parsed, outcome.State);
        Assert.Equal(TestOpenId, outcome.Event!.ExternalUserId);
    }

    /// <summary>D-9：AuthenticateAsync 无 FromUserId 事件（check_url / user_add_org）→ 放行
    /// SsoChannelAuthResult(true, null, null, 0)（P1-2——不再 DINGTALK_EVENT_DECRYPT_FAILED 400）；
    /// 带 FromUserId 事件行为不变（既有断言保持）。</summary>
    [Theory]
    [InlineData("check_url")]
    [InlineData("user_add_org")]
    public async Task AuthenticateAsync_IdentityLessEvent_Releases(string eventType)
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildIdentityLessEvent(eventType);

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ExternalUserId);
        Assert.Null(result.FailReason);
        Assert.Equal(0, result.AuthLevel);                                    // 放行语义（非身份认证强级）
    }
}
