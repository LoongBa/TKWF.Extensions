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
}
