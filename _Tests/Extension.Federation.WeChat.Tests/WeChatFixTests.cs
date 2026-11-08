using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace TKWF.Federation.WeChat.Tests;

/// <summary>
/// B 档修复测试（FW-06/FW-07）——WeChatApiClient token 失效缓存键清理 + WeChatEventCrypto 空 ToUserName 容错。
/// <para>FW-06：微信返回 40001/42001（凭证失效/token 过期）→ 失效缓存键被清除，下次调用重新获取
/// （断言经反射观察 _tokenCache 清空 + 调用计数证明重新请求）。</para>
/// <para>FW-07：解密明文尾部 receiveid 与 &lt;ToUserName&gt; 一致性观察——缺节点/空值不 NRE（最小容错，保持现行为）。</para>
/// </summary>
public class WeChatFixTests
{
    private const string AppId = "wx-app-token-cache";
    private const string TestEncodingAESKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"; // 43 字符

    /// <summary>按调用次序依次返回预设 JSON 的 token 桩 handler（计数 cgi-bin/token 请求）。</summary>
    private sealed class SequencingTokenHandler : HttpMessageHandler
    {
        private readonly string[] _responses;
        public int TokenRequestCount { get; private set; }

        public SequencingTokenHandler(params string[] responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/cgi-bin/token", StringComparison.Ordinal) == true)
            {
                var json = _responses[Math.Min(TokenRequestCount, _responses.Length - 1)];
                TokenRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static WeChatApiClient CreateClient(HttpMessageHandler handler, string appId)
        => new(
            new HttpClient(handler),
            new OptionsWeChatCredentialResolver(Options.Create(new WeChatOptions
            {
                Channels = [new WeChatChannelConfig { ChannelId = "mp", AppId = appId, AppSecret = "secret" }],
            })),
            NullLogger<WeChatApiClient>.Instance);

    /// <summary>反射读取 L1 缓存条目数（_tokenCache——观察缓存键被清除；非泛型 ICollection 规避私有 record 泛型不可变）。</summary>
    private static int CachedKeyCount(WeChatApiClient client)
    {
        var dict = typeof(WeChatApiClient)
            .GetField("_tokenCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client)!;
        return ((ICollection)dict).Count;
    }

    /// <summary>FW-06：token 请求返回 42001 → 抛异常且失效缓存键被清除 → 再次调用重新请求成功（缓存空可观察）。</summary>
    [Fact]
    public async Task GetAccessToken_42001_ClearsCacheAndRefetches()
    {
        // expires_in=1 使缓存条目立即过期（提前 5 分钟刷新语义）——每次调用都真实触发请求；
        // 首次成功种缓存 → 42001 抛异常并清失效键（反射可观察缓存空）→ 第三次重新请求取到新 token
        var handler = new SequencingTokenHandler(
            """{"access_token":"tok-1","expires_in":1}""",
            """{"errcode":42001,"errmsg":"access_token expired"}""",
            """{"access_token":"tok-2","expires_in":1}""");
        var client = CreateClient(handler, AppId);

        var first = await client.GetAccessTokenAsync(AppId, "mp", CancellationToken.None);
        Assert.Equal("tok-1", first);
        Assert.Equal(1, CachedKeyCount(client));   // 成功路径已写缓存

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAccessTokenAsync(AppId, "mp", CancellationToken.None));
        Assert.Contains("42001", ex.Message);

        Assert.Equal(0, CachedKeyCount(client));    // FW-06：42001 → 失效缓存键被清除（反射可观察缓存空）

        var third = await client.GetAccessTokenAsync(AppId, "mp", CancellationToken.None);
        Assert.Equal("tok-2", third);        // 缓存空 → 重新请求新 token
        Assert.Equal(3, handler.TokenRequestCount);   // 三次均真实请求（无缓存短路）
    }

    /// <summary>FW-06：40001（invalid credential）同清缓存键语义。</summary>
    [Fact]
    public async Task GetAccessToken_40001_ClearsCache()
    {
        var handler = new SequencingTokenHandler(
            """{"errcode":40001,"errmsg":"invalid credential"}""");
        var client = CreateClient(handler, AppId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAccessTokenAsync(AppId, "mp", CancellationToken.None));

        Assert.Equal(0, CachedKeyCount(client));    // 40001 → 失效缓存键被清除
        Assert.Equal(1, handler.TokenRequestCount);
    }

    /// <summary>FW-07：空 ToUserName 解密场景——解密成功（最小容错不 NRE），
    /// 后续提取 ToUserName 抛 InvalidOperationException（缺节点优雅拒绝，非 NullReferenceException）。</summary>
    [Fact]
    public void DecryptMsg_EmptyToUserName_NoNre_ToleranceKept()
    {
        var xml = "<xml><ToUserName></ToUserName><FromUserName><![CDATA[openid-empty-to]]></FromUserName>"
            + "<CreateTime>1234567890</CreateTime><MsgType><![CDATA[event]]></MsgType><Event><![CDATA[CLICK]]></Event></xml>";
        var msgEncrypt = WeChatEventCrypto.EncryptMsg(xml, TestEncodingAESKey, "receiveid");

        // 最小容错：解密正常返回（保持现行为），ToUserName 解析 null 条件访问 → 不 NRE
        var decrypted = WeChatEventCrypto.DecryptMsg(TestEncodingAESKey, msgEncrypt);
        Assert.Contains("<FromUserName>", decrypted);

        // 后续提取面优雅拒绝（提取方法抛 InvalidOperationException 而非 NRE）
        var ex = Assert.Throws<InvalidOperationException>(() => WeChatEventCrypto.ExtractToUserName(decrypted));
        Assert.Contains("ToUserName", ex.Message);
    }

    /// <summary>FW-07：畸形密文（篡改明文 msg_len 越界）→ CryptographicException（fail-closed 长度校验不受影响）。</summary>
    [Fact]
    public void DecryptMsg_MalformedMsgLength_ThrowsCryptographic()
    {
        var xml = "<xml><ToUserName><![CDATA[gh_test]]></ToUserName><FromUserName><![CDATA[openid-abc]]></FromUserName>"
            + "<CreateTime>1234567890</CreateTime><MsgType><![CDATA[text]]></MsgType><Content><![CDATA[hi]]></Content></xml>";
        var msgEncrypt = WeChatEventCrypto.EncryptMsg(xml, TestEncodingAESKey, "receiveid");

        // 解密 → 篡改明文 msg_len 高字节 255（20+msgLen 越界）→ 重加密 → 解密侧须抛 CryptographicException
        var aesKey = Convert.FromBase64String(TestEncodingAESKey + "=");
        var iv = aesKey.AsSpan(0, 16).ToArray();
        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] tampered;
        using (var decryptor = aes.CreateDecryptor())
        {
            var p = decryptor.TransformFinalBlock(Convert.FromBase64String(msgEncrypt), 0, Convert.FromBase64String(msgEncrypt).Length);
            p[17] = 0xFF;   // msg_len 高字节 255 → 20 + msgLen 越界
            using var encryptor = aes.CreateEncryptor();
            tampered = encryptor.TransformFinalBlock(p, 0, p.Length);
        }

        Assert.Throws<CryptographicException>(
            () => WeChatEventCrypto.DecryptMsg(TestEncodingAESKey, Convert.ToBase64String(tampered)));
    }
}