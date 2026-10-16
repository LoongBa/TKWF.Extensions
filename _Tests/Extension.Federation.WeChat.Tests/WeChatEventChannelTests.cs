using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;
using Xunit;

namespace TKWF.Federation.WeChat.Tests;

/// <summary>
/// <see cref="WeChatEventChannel"/>（wechat_event——公众号事件推送验签入站）测试——生产路径宿主
/// （真实 DI + <c>AddWeChatFederationChannels</c> + 探针门面帧内枚举）。
/// <para><b>信任根一票否决</b>（设计文档 §8.1）：合法 msg_signature 通过；篡改签名 → 拒
/// （WECHAT_SIGNATURE_INVALID）；参数缺失 → 拒；密文损坏 → 解密失败拒。</para>
/// <para><b>事件链（外部身份联邦 E3）</b>：<see cref="WeChatEventChannel.HandleEventAsync"/> 验签解密 +
/// 提取 MsgType/Event 富化 Parameters（评审 F4）+ handler 委托包微信 XML 被动回复（text/xml）；
/// handler 委托为空 → 200 空回复（微信 5 秒超时兜底）；验签失败 → null（装配层 AuthenticateAsync 先行映射）。</para>
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
        Assert.Null(result.ExternalUserId);
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

    /// <summary>HandleEventAsync 合法事件：验签解密 → MsgType/Event 提取到 Parameters（评审 F4）→ handler 业务内容
    /// 包微信 XML 被动回复（text/xml——ToUserName=原 FromUserName(openid)/FromUserName=原 ToUserName(公众号)）。</summary>
    [Fact]
    public async Task HandleEventAsync_ValidEvent_ExtractsMsgTypeEvent_ReturnsXmlReply()
    {
        using var _ = this;
        var handler = new FakeEventReplyHandler
        {
            Result = new EventReplyResult("登录链接：https://sso.example.com/landing?ticket=t-1", "text/plain"),
        };
        var channel = await Setup(svc => svc.AddScoped<IEventReplyHandler>(_ => handler));
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        var context = WeChatTestHost.CreateEventContext(
            ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt));

        var reply = await channel.HandleEventAsync(context, CancellationToken.None);

        // 微信 XML 被动回复（text/xml）
        Assert.NotNull(reply);
        Assert.Equal("text/xml", reply!.ContentType);
        Assert.Contains("<ToUserName><![CDATA[oa_openid_event_1]]></ToUserName>", reply.Content); // 回复对象 = openid（原 FromUserName）
        Assert.Contains("<FromUserName><![CDATA[gh_test]]></FromUserName>", reply.Content);        // 公众号 id（原 ToUserName）
        Assert.Contains("<MsgType><![CDATA[text]]></MsgType>", reply.Content);
        Assert.Contains("<Content><![CDATA[登录链接：https://sso.example.com/landing?ticket=t-1]]></Content>", reply.Content);

        // 评审 F4——MsgType/Event 提取到 Parameters（共享字典原地富化）
        Assert.Equal("event", context.Parameters["msg_type"]);
        Assert.Equal("CLICK", context.Parameters["event"]);
        Assert.Equal(TestOpenId, context.Parameters["external_user_id"]);
    }

    /// <summary>HandleEventAsync 无 handler 注册（业务回复缺失）→ 200 空回复（text/xml）——微信 5 秒超时约束兜底
    /// （异步客服消息机制归 Phase 2/消费方）；Parameters 仍富化（MsgType/Event 提取不依赖 handler）。</summary>
    [Fact]
    public async Task HandleEventAsync_NoHandler_ReturnsEmpty200()
    {
        using var _ = this;
        var channel = await Setup();   // 无 handler 注册（平台库不注册 handler，防过度设计）
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        var context = WeChatTestHost.CreateEventContext(
            ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt));

        var reply = await channel.HandleEventAsync(context, CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal("", reply!.Content);    // 空 Content = 200 空回复
        Assert.Equal("text/xml", reply.ContentType);
        Assert.Equal("event", context.Parameters["msg_type"]);   // 富化不依赖 handler
        Assert.Equal("CLICK", context.Parameters["event"]);
    }

    /// <summary>HandleEventAsync 验签失败（篡改签名——信任根一票否决）→ null（错误语义由装配层 AuthenticateAsync 先行映射 4xx）。</summary>
    [Fact]
    public async Task HandleEventAsync_TamperedSignature_ReturnsNull()
    {
        using var _ = this;
        var channel = await Setup();
        var (msgEncrypt, timestamp, nonce, signature) = BuildValidEvent();
        var tampered = signature[..^1] + (signature[^1] == '0' ? '1' : '0');

        var reply = await channel.HandleEventAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_signature", tampered), ("timestamp", timestamp), ("nonce", nonce), ("msg_encrypt", msgEncrypt)),
            CancellationToken.None);

        Assert.Null(reply);   // 验签失败 → null（绝不解密伪造密文）
    }

    /// <summary>HandleEventAsync 模板实例（channel=null）→ null（不可认证语义对齐——工厂帧内构造真实实例）。</summary>
    [Fact]
    public async Task HandleEventAsync_TemplateInstance_ReturnsNull()
    {
        using var _ = this;
        // 模板实例 = 集合类型索引源（不直接认证）——直构（StubDomainUser + 空 handler 集合）
        var channel = new WeChatEventChannel(new StubDomainUser(), []);
        var reply = await channel.HandleEventAsync(
            WeChatTestHost.CreateEventContext(("msg_signature", "x"), ("timestamp", "1"), ("nonce", "n"), ("msg_encrypt", "c")),
            CancellationToken.None);
        Assert.Null(reply);
    }

    /// <summary>测试 handler（IEventReplyHandler——HandleEventAsync 用例；按 ChannelType=wechat_event 匹配）。</summary>
    private sealed class FakeEventReplyHandler : IEventReplyHandler
    {
        public string ChannelType => "wechat_event";
        public EventReplyResult? Result { get; set; }
        public int Invoked { get; private set; }

        public Task<EventReplyResult?> HandleAsync(SsoChannelEventContext context, CancellationToken ct = default)
        {
            Invoked++;
            return Task.FromResult(Result);
        }
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
        var channel = new WeChatEventChannel(new StubDomainUser(), []);
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
    /// 验签解密 → MsgType/Content 富化 → handler 图文/文本回复。</summary>
    [Fact]
    public async Task HandleEventAsync_PushEncryptInBodyXml_ExtractsContent_ReturnsXmlReply()
    {
        using var _ = this;
        var handler = new FakeEventReplyHandler
        {
            Result = new EventReplyResult("登录链接：https://sso.example.com/landing?ticket=t-1", "text/plain"),
        };
        var channel = await Setup(svc => svc.AddScoped<IEventReplyHandler>(_ => handler));
        var (_, timestamp, nonce, signature, rawBody) = BuildValidPush();
        var context = WeChatTestHost.CreateEventContext(
            ("msg_signature", signature), ("timestamp", timestamp), ("nonce", nonce), ("__raw", rawBody));

        var reply = await channel.HandleEventAsync(context, CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal("text/xml", reply!.ContentType);
        Assert.Contains("<MsgType><![CDATA[text]]></MsgType>", reply.Content);
        // 富化验证：msg_type=text + content=消息原文（图文 Description 源）+ openid
        Assert.Equal("text", context.Parameters["msg_type"]);
        Assert.Equal("你好，欢迎使用", context.Parameters["content"]);
        Assert.Equal(TestOpenId, context.Parameters["external_user_id"]);
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
}
