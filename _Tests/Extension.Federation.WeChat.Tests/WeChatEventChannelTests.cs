using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using Xunit;

namespace TKWF.Federation.WeChat.Tests;

/// <summary>
/// <see cref="WeChatEventChannel"/>（wechat_event——公众号事件推送验签入站）测试——生产路径宿主
/// （真实 DI + <c>AddWeChatFederationChannels</c> + 探针门面帧内枚举）。
/// <para><b>信任根一票否决</b>（设计文档 §8.1）：合法 msg_signature 通过；篡改签名 → 拒
/// （WECHAT_SIGNATURE_INVALID）；参数缺失 → 拒；密文损坏 → 解密失败拒。</para>
/// <para><b>事件链（外部身份联邦 E3——2026-10-17 方案 §3.4 改造）</b>：<see cref="WeChatEventChannel.HandleEventAsync"/>
/// 验签解密 + 提取 MsgType/Event/Content 富化 Parameters（评审 F4）+ <c>__decrypted_xml</c> 明文 XML 富化
/// （Oracle P1-4——平台基类 Parse 数据源）→ <b>返回 null</b>（无平台回复——编排器转 handler 双桶迭代，
/// 200 空回复兜底在端点层微信 5 秒超时约束）；验签失败 → null（装配层 AuthenticateAsync 先行映射 4xx）。</para>
/// </summary>
public class WeChatEventChannelTests : WeChatChannelTestHost
{
    private const string TestToken = "test-token";
    private const string TestAppId = "wx0000000000000000";
    private const string TestAesKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"; // 43 字符
    private const string TestOpenId = "oa_openid_event_1";
    private const string TestChannelId = "mp-event";

    private async Task<WeChatEventChannel> Setup(Action<IServiceCollection>? configure = null)
    {
        ConfigureOptions = o => o.Channels =
        [
            WeChatTestHost.CreateChannelConfig(channelId: TestChannelId, appId: TestAppId, token: TestToken, encodingAESKey: TestAesKey),
        ];
        var (_, user) = BindTestScope(configure);
        // v0.3.0：经 ISsoChannelFactory 按 channelId 构造真实实例（凭证 Token/EncodingAESKey 从 registry Extra 懒加载）
        var channel = await user.Use<ISsoChannelFactory>().CreateAsync(TestChannelId, "wechat_event");
        return Assert.IsType<WeChatEventChannel>(channel);
    }

    /// <summary>构造合法事件推送样本（msg_encrypt + msg_signature），openid 嵌入 FromUserName。</summary>
    private static (string MsgEncrypt, string Timestamp, string Nonce, string Signature) BuildValidEvent(string openId = TestOpenId)
    {
        var xml = $"<xml><ToUserName><![CDATA[gh_test]]></ToUserName><FromUserName><![CDATA[{openId}]]></FromUserName><CreateTime>1234567890</CreateTime><MsgType><![CDATA[event]]></MsgType><Event><![CDATA[CLICK]]></Event></xml>";
        var msgEncrypt = WeChatEventCrypto.EncryptMsg(xml, TestAesKey, TestAppId);
        var timestamp = "1234567890";
        var nonce = "test-nonce";
        var signature = WeChatEventCrypto.ComputeSignature(TestToken, timestamp, nonce, msgEncrypt);
        return (msgEncrypt, timestamp, nonce, signature);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSignatureAndCipher_ReturnsOpenId()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenId, result.ExternalUserId); // FromUserName = openid
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_SignatureAliasKey_Accepts()
    {
        // 兼容 "signature" 参数名（装配层可能透传该命名）
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenId, result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_TamperedSignature_Rejected_WECHAT_SIGNATURE_INVALID()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        // 篡改签名末位——信任根一票否决
        var tampered = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("msg_signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECHAT_SIGNATURE_INVALID", result.FailReason);
    }

    // ═══════════════════ I2-7 语义层精化（归一化/Capabilities/关注状态反查） ═══════════════════

    /// <summary>构造指定 MsgType/Event/EventKey 的合法事件（验签解密全链——I2-7 归一化正分支）。</summary>
    private static (string MsgEncrypt, string Timestamp, string Nonce, string Signature) BuildEvent(
        string eventValue, string? eventKey = null, string msgType = "event")
    {
        var keyXml = eventKey is null ? "" : $"<EventKey><![CDATA[{eventKey}]]></EventKey>";
        var xml = $"<xml><ToUserName><![CDATA[gh_test]]></ToUserName><FromUserName><![CDATA[{TestOpenId}]]></FromUserName>"
            + $"<CreateTime>1234567890</CreateTime><MsgType><![CDATA[{msgType}]]></MsgType><Event><![CDATA[{eventValue}]]></Event>{keyXml}</xml>";
        var msgEncrypt = WeChatEventCrypto.EncryptMsg(xml, TestAesKey, TestAppId);
        var timestamp = "1234567890";
        var nonce = "test-nonce";
        var signature = WeChatEventCrypto.ComputeSignature(TestToken, timestamp, nonce, msgEncrypt);
        return (msgEncrypt, timestamp, nonce, signature);
    }

    private static async Task<ChannelEvent> ParseAsync(WeChatEventChannel channel, (string, string, string, string) msg)
    {
        var (encrypt, timestamp, nonce, signature) = msg;
        var outcome = await channel.HandleEventAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", encrypt)),
            CancellationToken.None);
        Assert.Equal(ChannelEventOutcomeState.Parsed, outcome.State);
        return outcome.Event!;
    }

    /// <summary>subscribe（无 EventKey）→ follow（普通关注——§3.3 ①）。</summary>
    [Fact]
    public async Task HandleEventAsync_Subscribe_NormalizesToFollow()
    {
        using var _ = this;
        var channel = await Setup();
        var evt = await ParseAsync(channel, BuildEvent("subscribe"));
        Assert.Equal("follow", evt.EventType);
        Assert.Null(evt.EventKey);
        Assert.Null(evt.Source);
    }

    /// <summary>subscribe + qrscene_ 前缀 → scan_follow（扫码关注——EventKey 去前缀 + Source=scene id，§3.3 ①）。</summary>
    [Fact]
    public async Task HandleEventAsync_SubscribeWithScene_NormalizesToScanFollow()
    {
        using var _ = this;
        var channel = await Setup();
        var evt = await ParseAsync(channel, BuildEvent("subscribe", "qrscene_1001"));
        Assert.Equal("scan_follow", evt.EventType);
        Assert.Equal("1001", evt.EventKey);
        Assert.Equal("1001", evt.Source);
    }

    /// <summary>unsubscribe → unfollow（§3.3 ②——取关）。</summary>
    [Fact]
    public async Task HandleEventAsync_Unsubscribe_NormalizesToUnfollow()
    {
        using var _ = this;
        var channel = await Setup();
        var evt = await ParseAsync(channel, BuildEvent("unsubscribe"));
        Assert.Equal("unfollow", evt.EventType);
    }

    /// <summary>SCAN（已关注扫码）→ scan_only（EventKey 去前缀 + Source，§3.3 ③——不翻状态）。</summary>
    [Fact]
    public async Task HandleEventAsync_Scan_NormalizesToScanOnly()
    {
        using var _ = this;
        var channel = await Setup();
        var evt = await ParseAsync(channel, BuildEvent("SCAN", "qrscene_2002"));
        Assert.Equal("scan_only", evt.EventType);
        Assert.Equal("2002", evt.EventKey);
        Assert.Equal("2002", evt.Source);
    }

    /// <summary>CLICK → menu_click（EventKey=菜单 key，§3.3 ④）。</summary>
    [Fact]
    public async Task HandleEventAsync_Click_NormalizesToMenuClick()
    {
        using var _ = this;
        var channel = await Setup();
        var evt = await ParseAsync(channel, BuildEvent("CLICK", "menu_main"));
        Assert.Equal("menu_click", evt.EventType);
        Assert.Equal("menu_main", evt.EventKey);
        Assert.Null(evt.Source);
    }

    /// <summary>其余事件（VIEW 等）→ 原文保留（记事实不翻状态，§3.3 ⑤）。</summary>
    [Fact]
    public async Task HandleEventAsync_View_KeepsOriginalType()
    {
        using var _ = this;
        var channel = await Setup();
        var evt = await ParseAsync(channel, BuildEvent("VIEW"));
        Assert.Equal("VIEW", evt.EventType);
    }

    /// <summary>OccurredAt = CreateTime（1234567890 → UTC 2009-02-13 23:31:30）；CreateTime 原文进 Attributes（wechat.CreateTime）。</summary>
    [Fact]
    public async Task HandleEventAsync_CreateTime_ConvertsToOccurredAtUtc()
    {
        using var _ = this;
        var channel = await Setup();
        var evt = await ParseAsync(channel, BuildEvent("subscribe"));
        var expected = DateTimeOffset.FromUnixTimeSeconds(1234567890).UtcDateTime;
        Assert.Equal(expected, evt.OccurredAt);
        Assert.Equal("1234567890", evt.Attributes["wechat.CreateTime"]);
    }

    /// <summary>RawPayload 保留解密 XML（I2-7 验收——handler/sync 透传数据源不断）。</summary>
    [Fact]
    public async Task HandleEventAsync_RawPayload_KeepsDecryptedXml()
    {
        using var _ = this;
        var channel = await Setup();
        var evt = await ParseAsync(channel, BuildEvent("subscribe", "qrscene_1001"));
        Assert.NotNull(evt.RawPayload);
        Assert.Contains("qrscene_1001", evt.RawPayload!);
        Assert.Contains(TestOpenId, evt.RawPayload!);
    }

    /// <summary>Capabilities：微信首批声明 Event|Follow|FollowQuery|Menu + SilentAuthorize（认证服务号配置）。</summary>
    [Fact]
    public async Task Capabilities_DeclaresEventFollowFollowQueryMenu()
    {
        using var _ = this;
        await Setup();
        // Capabilities 在 IChannelSource（平台级能力声明——§3.4）；经 DI 解析真实 WeChatChannelSource
        var (provider, _) = BindTestScope();
        var source = provider.GetRequiredService<IChannelSource>();
        var caps = source.Capabilities;
        Assert.True(caps.HasFlag(ChannelCapabilities.Event));
        Assert.True(caps.HasFlag(ChannelCapabilities.Follow));
        Assert.True(caps.HasFlag(ChannelCapabilities.FollowQuery));
        Assert.True(caps.HasFlag(ChannelCapabilities.Menu));
        Assert.True(caps.HasFlag(ChannelCapabilities.SilentAuthorize));   // 测试配置含 AppId/AppSecret
    }

    /// <summary>关注状态反查（IFollowChannelQuery——经 cgi-bin/user/info subscribe 字段；对账路径）。</summary>
    [Fact]
    public async Task QueryFollowState_Subscribed_ReturnsFollowingTrue()
    {
        using var _ = this;
        // 反查走出站 ApiClient（基类 Stub IHttpClientFactory 路由 cgi-bin/user/info——subscribe=1）
        var channel = await Setup();
        var svc = Assert.IsType<WeChatEventChannel>(channel);

        var snap = await svc.QueryFollowStateAsync("oa_openid_event_1", CancellationToken.None);
        Assert.NotNull(snap);
        Assert.True(snap!.IsFollowing);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongLengthSignature_Rejected_NoThrow()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, _) = BuildValidEvent();

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("msg_signature", "short-sig"), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECHAT_SIGNATURE_INVALID", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingParams_Rejected_WECHAT_EVENT_PARAMS_MISSING()
    {
        using var _ = this;
        var channel = await Setup();

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(("timestamp", "1234567890")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECHAT_EVENT_PARAMS_MISSING", result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidSignatureTamperedCipher_DecryptFails()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        // 篡改密文（换首字符）——重签指向篡改密文（验签通过）→ 解密失败（填充/格式损坏）
        var tamperedCipher = (msgEncrypt[0] == 'A' ? 'B' : 'A') + msgEncrypt[1..];
        var signatureForTampered = WeChatEventCrypto.ComputeSignature(TestToken, timestamp, nonce, tamperedCipher);

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("msg_signature", signatureForTampered), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", tamperedCipher)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECHAT_EVENT_DECRYPT_FAILED", result.FailReason);
    }

    // ═══════════════════ 事件链（外部身份联邦 E3——HandleEventAsync） ═══════════════════

    /// <summary>HandleEventAsync 合法事件：验签解密 → 解析为语义 ChannelEvent（I2——MsgType/Event 提取到
    /// evt.EventType + Attributes，RawPayload 承载解密 XML——handler 重建数据源）。</summary>
    [Fact]
    public async Task HandleEventAsync_ValidEvent_ExtractsMsgTypeEvent_ReturnsNull()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        var context = WeChatTestHost.CreateEventContext(
            ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt));

        var outcome = await channel.HandleEventAsync(context, CancellationToken.None);

        // I2 语义层：合法事件 → Parsed（不再返回 null——通道产语义事件，编排消费）
        Assert.Equal(ChannelEventOutcomeState.Parsed, outcome.State);
        var evt = Assert.IsType<ChannelEvent>(outcome.Event);
        // 评审 F4——MsgType/Event 提取到语义事件（Attributes 承载；Parameters 只留原始 query）
        Assert.Equal("event", evt.Attributes["wechat.MsgType"]);
        Assert.Equal("CLICK", evt.Attributes["wechat.Event"]);
        Assert.Equal(TestOpenId, evt.ExternalUserId);
        // Oracle P1-4——解密后明文 XML 承载 RawPayload（handler WechatEventContext.FromChannelEvent 重建数据源）
        Assert.NotNull(evt.RawPayload);
        Assert.Contains(TestOpenId, evt.RawPayload!);
    }

    /// <summary>HandleEventAsync 正常路径产 Parsed（I2 语义层——通道产语义事件，编排器消费后转 handler；
    /// 微信 5 秒超时约束语义保持：回复归端点 200 空回复兜底）。</summary>
    [Fact]
    public async Task HandleEventAsync_ValidEvent_ReturnsNull_NoPlatformReply()
    {
        using var _ = this;
        var channel = await Setup();   // 无 handler 注册（平台库不注册 handler，防过度设计）
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        var context = WeChatTestHost.CreateEventContext(
            ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt));

        var outcome = await channel.HandleEventAsync(context, CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.Parsed, outcome.State);   // I2：合法事件 → Parsed（通道不产回复——回复归 handler/端点）
        var evt = outcome.Event!;
        Assert.Equal("event", evt.Attributes["wechat.MsgType"]);   // 富化不依赖 handler（Attributes 承载）
        Assert.Equal("CLICK", evt.Attributes["wechat.Event"]);
        Assert.NotNull(evt.RawPayload);
    }

    /// <summary>HandleEventAsync 验签失败（篡改签名——信任根一票否决）→ Rejected（编排短路：不记录、不回复、不回退 handler）。</summary>
    [Fact]
    public async Task HandleEventAsync_TamperedSignature_ReturnsNull()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        var tampered = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var outcome = await channel.HandleEventAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.Rejected, outcome.State);   // 验签失败 → Rejected（绝不解密伪造密文——信任根一票否决）
    }

    /// <summary>HandleEventAsync 模板实例（channel=null）→ NotApplicable（不可认证语义对齐——工厂帧内构造真实实例）。</summary>
    [Fact]
    public async Task HandleEventAsync_TemplateInstance_ReturnsNull()
    {
        using var _ = this;
        // 模板实例 = 集合类型索引源（不直接认证）——直构（StubDomainUser；ctor 已精简为
        // (IDomainUser, ChannelConfig?, ILogger?)——2026-10-17 方案 §3.4 删 handler 集合参数）
        var channel = new WeChatEventChannel(new StubDomainUser());
        var outcome = await channel.HandleEventAsync(
            WeChatTestHost.CreateEventContext(("msg_signature", "x"), ("timestamp", "1"), ("nonce", "n"), ("msg_encrypt", "c")),
            CancellationToken.None);
        Assert.Equal(ChannelEventOutcomeState.NotApplicable, outcome.State);
    }

    // ═══════════════════ URL 验证握手（微信后台服务器配置验证契约——VerifyUrlAsync） ═══════════════════

    /// <summary>构造合法 URL 验证样本（安全模式：echostr 密文 + msg_signature 4 参；明文模式：echostr 原文 + signature 3 参）。</summary>
    private static (string EchoCipher, string Timestamp, string Nonce, string MsgSignature, string Signature3, string EchoPlain) BuildValidUrlVerify()
    {
        var echoPlain = "4375120948345356249";   // 微信后台随机串（明文模式回显原文）
        var timestamp = "1714036504";
        var nonce = "1514711492";
        // 安全模式：echostr = AES 密文（结构 random16+len4+msg+appid——EncryptMsg 复用），msg_signature 含 echostr 密文
        var echoCipher = WeChatEventCrypto.EncryptMsg(echoPlain, TestAesKey, TestAppId);
        var msgSignature = WeChatEventCrypto.ComputeSignature(TestToken, timestamp, nonce, echoCipher);
        // 明文模式：signature 3 参（不含消息体）
        var signature3 = WeChatEventCrypto.ComputeSignature3(TestToken, timestamp, nonce);
        return (echoCipher, timestamp, nonce, msgSignature, signature3, echoPlain);
    }

    /// <summary>安全模式 URL 验证：msg_signature 4 参验签通过 + AES 解密 echostr → 回显<b>解密明文</b>
    /// （回显密文必失败——微信官方 WXBizMsgCrypt.VerifyURL 语义）。</summary>
    [Fact]
    public async Task VerifyUrlAsync_SecurityMode_VerifiesAndDecryptsEcho()
    {
        using var _ = this;
        var channel = await Setup();
        var (echoCipher, timestamp, nonce, msgSignature, _, echoPlain) = BuildValidUrlVerify();

        var plain = await channel.VerifyUrlAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_signature", msgSignature), ("timestamp", timestamp), ("nonce", nonce), ("echostr", echoCipher)),
            CancellationToken.None);

        Assert.Equal(echoPlain, plain);   // 解密明文（非密文回显）
    }

    /// <summary>明文模式 URL 验证：signature 3 参验签通过 → 原样回显 echostr 原文。</summary>
    [Fact]
    public async Task VerifyUrlAsync_PlainTextMode_VerifiesSignature3_EchoesRaw()
    {
        using var _ = this;
        var channel = await Setup();
        var (echoCipher, timestamp, nonce, _, signature3, echoPlain) = BuildValidUrlVerify();

        var plain = await channel.VerifyUrlAsync(
            WeChatTestHost.CreateEventContext(
                ("signature", signature3), ("timestamp", timestamp), ("nonce", nonce), ("echostr", echoPlain)),
            CancellationToken.None);

        Assert.Equal(echoPlain, plain);   // 明文回显原文
    }

    /// <summary>安全模式 URL 验证：篡改 msg_signature → null（信任根一票否决——验签失败拒，绝不解密伪造密文）。</summary>
    [Fact]
    public async Task VerifyUrlAsync_SecurityMode_TamperedMsgSignature_ReturnsNull()
    {
        using var _ = this;
        var channel = await Setup();
        var (echoCipher, timestamp, nonce, msgSignature, _, _) = BuildValidUrlVerify();
        var tampered = msgSignature[..^1] + (msgSignature[^1] == '0' ? '1' : '0');

        var plain = await channel.VerifyUrlAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("echostr", echoCipher)),
            CancellationToken.None);

        Assert.Null(plain);
    }

    /// <summary>明文模式 URL 验证：篡改 signature → null（信任根一票否决）。</summary>
    [Fact]
    public async Task VerifyUrlAsync_PlainTextMode_TamperedSignature_ReturnsNull()
    {
        using var _ = this;
        var channel = await Setup();
        var (_, timestamp, nonce, _, signature3, echoPlain) = BuildValidUrlVerify();
        var tampered = signature3[..^1] + (signature3[^1] == '0' ? '1' : '0');

        var plain = await channel.VerifyUrlAsync(
            WeChatTestHost.CreateEventContext(
                ("signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("echostr", echoPlain)),
            CancellationToken.None);

        Assert.Null(plain);
    }

    /// <summary>URL 验证缺参（无 timestamp/echostr）→ null（参数缺失——微信后台"token 验证失败"）。</summary>
    [Fact]
    public async Task VerifyUrlAsync_MissingParams_ReturnsNull()
    {
        using var _ = this;
        var channel = await Setup();

        var plain = await channel.VerifyUrlAsync(
            WeChatTestHost.CreateEventContext(("timestamp", "1234567890")),
            CancellationToken.None);

        Assert.Null(plain);
    }

    /// <summary>URL 验证模板实例（channel=null）→ null（不可认证语义对齐）。</summary>
    [Fact]
    public async Task VerifyUrlAsync_TemplateInstance_ReturnsNull()
    {
        using var _ = this;
        var channel = new WeChatEventChannel(new StubDomainUser());
        var plain = await channel.VerifyUrlAsync(
            WeChatTestHost.CreateEventContext(("msg_signature", "x"), ("timestamp", "1"), ("nonce", "n"), ("echostr", "c")),
            CancellationToken.None);
        Assert.Null(plain);
    }

    // ═══════════════════ POST 消息推送（安全模式——msg_encrypt 在 body XML <Encrypt>） ═══════════════════

    /// <summary>构造合法安全模式 POST 推送样本：body = 微信封装 XML（&lt;Encrypt&gt; 密文——内部再嵌消息 XML），
    /// query 带 msg_signature（4 参 SHA1 含 Encrypt 密文）+ timestamp/nonce；__raw = body 封装 XML。</summary>
    private static (string Encrypt, string Timestamp, string Nonce, string Signature, string RawBody) BuildValidPush()
    {
        // 内层消息 XML（FromUserName=openid + 文本 Content）
        var innerXml = $"<xml><ToUserName><![CDATA[gh_test]]></ToUserName><FromUserName><![CDATA[{TestOpenId}]]></FromUserName><CreateTime>1234567890</CreateTime><MsgType><![CDATA[text]]></MsgType><Content><![CDATA[你好，欢迎使用]]></Content><MsgId>1234567890123456</MsgId></xml>";
        // 外层封装 XML：<Encrypt> 密文（EncryptMsg 产物）
        var encrypt = WeChatEventCrypto.EncryptMsg(innerXml, TestAesKey, TestAppId);
        var rawBody = $"<xml><ToUserName><![CDATA[gh_test]]></ToUserName><Encrypt><![CDATA[{encrypt}]]></Encrypt></xml>";
        var timestamp = "1714112445";
        var nonce = "415670741";
        var signature = WeChatEventCrypto.ComputeSignature(TestToken, timestamp, nonce, encrypt);
        return (encrypt, timestamp, nonce, signature, rawBody);
    }

    /// <summary>安全模式 POST 推送：msg_encrypt 不在 query（在 body XML &lt;Encrypt&gt;，经 __raw 提取）→
    /// 验签解密通过 → openid + 富化 Parameters（external_user_id/content）。</summary>
    [Fact]
    public async Task AuthenticateAsync_PushEncryptInBodyXml_ExtractsFromRaw()
    {
        using var _ = this;
        var channel = await Setup();
        var (encrypt, timestamp, nonce, signature, rawBody) = BuildValidPush();

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("__raw", rawBody)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenId, result.ExternalUserId);   // FromUserName = openid
        Assert.Null(result.FailReason);
    }

    /// <summary>安全模式 POST 推送 HandleEventAsync：msg_encrypt 从 body XML &lt;Encrypt&gt;（__raw）提取 →
    /// 验签解密 → 解析为语义 ChannelEvent（MsgType/Content/Attributes 承载）。</summary>
    [Fact]
    public async Task HandleEventAsync_PushEncryptInBodyXml_ExtractsContent_ReturnsNull()
    {
        using var _ = this;
        var channel = await Setup();
        var (_, timestamp, nonce, signature, rawBody) = BuildValidPush();
        var context = WeChatTestHost.CreateEventContext(
            ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("__raw", rawBody));

        var outcome = await channel.HandleEventAsync(context, CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.Parsed, outcome.State);   // I2：合法事件 → Parsed
        var evt = outcome.Event!;
        // 富化验证：msg_type=text + content=消息原文（图文 Description 源）+ openid + 归一 message（I2-7）
        Assert.Equal("text", evt.Attributes["wechat.MsgType"]);
        Assert.Equal("你好，欢迎使用", evt.Attributes["wechat.Content"]);
        Assert.Equal("message", evt.EventType);   // 消息类归一（§3.3 ⑥）
        Assert.Equal(TestOpenId, evt.ExternalUserId);
        // Oracle P1-4——解密后明文 XML 承载 RawPayload（__raw 提取 Encrypt → 解密）
        Assert.NotNull(evt.RawPayload);
        Assert.Contains("你好，欢迎使用", evt.RawPayload!);
    }

    /// <summary>安全模式 POST 推送验签失败（篡改签名）→ null（信任根一票否决——从 __raw 提取的 Encrypt 参与验签）。</summary>
    [Fact]
    public async Task AuthenticateAsync_PushEncryptInBodyXml_TamperedSignature_Rejected()
    {
        using var _ = this;
        var channel = await Setup();
        var (encrypt, timestamp, nonce, signature, rawBody) = BuildValidPush();
        var tampered = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("msg_signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("__raw", rawBody)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECHAT_SIGNATURE_INVALID", result.FailReason);
    }

    // ═══════════════════ POST 消息推送（明文模式 P1-1——body 即明文 XML，signature 3 参） ═══════════════════

    /// <summary>构造合法明文模式 POST 推送样本：body = 明文 XML（无 &lt;Encrypt&gt; 节点），
    /// query 带 signature（3 参 SHA1——不含消息体）+ timestamp/nonce；__raw = body 明文 XML。</summary>
    private static (string Timestamp, string Nonce, string Signature, string RawBody) BuildValidPlainTextPush(string openId = TestOpenId)
    {
        // 明文 XML（FromUserName=openid + 文本 Content）——无 Encrypt 封装
        var rawBody = $"<xml><ToUserName><![CDATA[gh_test]]></ToUserName><FromUserName><![CDATA[{openId}]]></FromUserName>"
            + "<CreateTime>1714333445</CreateTime><MsgType><![CDATA[text]]></MsgType><Content><![CDATA[明文模式你好]]></Content><MsgId>9988776655443322</MsgId></xml>";
        var timestamp = "1714333445";
        var nonce = "7d1630f1";
        var signature = WeChatEventCrypto.ComputeSignature3(TestToken, timestamp, nonce);
        return (timestamp, nonce, signature, rawBody);
    }

    /// <summary>明文模式 POST 推送 AuthenticateAsync：无 msg_encrypt（body 即明文 XML）→ 3 参验签通过 →
    /// 直接解析 openid（P1-1——此前明文 POST 被误拒 WECHAT_EVENT_PARAMS_MISSING）。</summary>
    [Fact]
    public async Task AuthenticateAsync_PlainTextMode_VerifiesSignature3_ReturnsOpenId()
    {
        using var _ = this;
        var channel = await Setup();
        var (timestamp, nonce, signature, rawBody) = BuildValidPlainTextPush();

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("__raw", rawBody)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenId, result.ExternalUserId);
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel);
    }

    /// <summary>明文模式 POST 推送 AuthenticateAsync：兼容 msg_signature 键承载 3 参签名值（装配层透传命名）。</summary>
    [Fact]
    public async Task AuthenticateAsync_PlainTextMode_MsgSignatureKey_Accepts()
    {
        using var _ = this;
        var channel = await Setup();
        var (timestamp, nonce, signature, rawBody) = BuildValidPlainTextPush();

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("__raw", rawBody)),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenId, result.ExternalUserId);
    }

    /// <summary>明文模式 POST 推送 HandleEventAsync：3 参验签 + body 明文 XML → 解析为语义 ChannelEvent
    /// （MsgType/Content/RawPayload=明文 XML——handler 数据链可用，P1-1）。</summary>
    [Fact]
    public async Task HandleEventAsync_PlainTextMode_ParsesChannelEvent()
    {
        using var _ = this;
        var channel = await Setup();
        var (timestamp, nonce, signature, rawBody) = BuildValidPlainTextPush();
        var context = WeChatTestHost.CreateEventContext(
            ("signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("__raw", rawBody));

        var outcome = await channel.HandleEventAsync(context, CancellationToken.None);

        Assert.Equal(ChannelEventOutcomeState.Parsed, outcome.State);
        var evt = outcome.Event!;
        Assert.Equal("text", evt.Attributes["wechat.MsgType"]);
        Assert.Equal("明文模式你好", evt.Attributes["wechat.Content"]);
        Assert.Equal("message", evt.EventType);          // 消息类归一
        Assert.Equal(TestOpenId, evt.ExternalUserId);
        Assert.NotNull(evt.RawPayload);
        Assert.Contains("明文模式你好", evt.RawPayload!);
    }

    /// <summary>明文模式 POST 推送熵改签名 → 拒（WECHAT_SIGNATURE_INVALID——信任根一票否决，
    /// 明文消息体不参与签名，验签为唯一伪造防线）。</summary>
    [Fact]
    public async Task AuthenticateAsync_PlainTextMode_TamperedSignature_Rejected()
    {
        using var _ = this;
        var channel = await Setup();
        var (timestamp, nonce, signature, rawBody) = BuildValidPlainTextPush();
        var tampered = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("__raw", rawBody)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECHAT_SIGNATURE_INVALID", result.FailReason);
    }

    /// <summary>明文模式 POST 推送缺 __raw（无明文 body）→ 拒（WECHAT_EVENT_PARAMS_MISSING——无法验签/解析）。</summary>
    [Fact]
    public async Task AuthenticateAsync_PlainTextMode_MissingRawBody_Rejected()
    {
        using var _ = this;
        var channel = await Setup();
        var (timestamp, nonce, signature, _) = BuildValidPlainTextPush();

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("signature", signature), ("timestamp", timestamp), ("nonce", nonce)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECHAT_EVENT_PARAMS_MISSING", result.FailReason);
    }

    /// <summary>明文模式 POST 推送缺 signature → 拒（WECHAT_EVENT_PARAMS_MISSING）。</summary>
    [Fact]
    public async Task AuthenticateAsync_PlainTextMode_MissingSignature_Rejected()
    {
        using var _ = this;
        var channel = await Setup();
        var (timestamp, nonce, _, rawBody) = BuildValidPlainTextPush();

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(
                ("timestamp", timestamp), ("nonce", nonce), ("__raw", rawBody)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECHAT_EVENT_PARAMS_MISSING", result.FailReason);
    }
}
