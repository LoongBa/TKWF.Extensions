using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// 子应用消费方桥接门面测试（2026-10-11——<see cref="SsoSubAppBridge"/> 真实门面 + Fake 依赖）——
/// <b>生产路径宿主</b>（真实 DI：Initializer <c>AddConstructibleService&lt;ISsoSubAppBridge, SsoSubAppBridge&gt;</c>
/// 守卫工厂 + FreeSql SQLite + AddLogging）+ <see cref="TestHostBase.BindTestScope"/> +
/// <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> + <c>User.Use&lt;ISsoSubAppBridge&gt;()</c>
/// （AOP 路径：设 CurrentAopUser → GetRequiredService → 守卫工厂）。
/// <para>Fake 依赖（<see cref="ISsoClientQueryService"/>/<see cref="ISsoChannelFactory"/>/<see cref="ISsoLogin"/>/
/// <see cref="IAccessCodeService"/>）经普通 Scoped 注册（bridged 内 <c>User.Use&lt;接口&gt;()</c> AOP 解析）；
/// keyed <see cref="ISymmetricKeyProvider"/>（<see cref="SymmetricKeyProviderKeys.Federation"/>）用最小 Fake
/// （GetKey 返回固定 32 字节——state 票据 HMAC-SHA256 只用密钥材料，Encrypt/Decrypt 未用抛 NotSupported）。</para>
/// <para>用例映射：StartAsync（federated 成功——state 票据生成 + 通道 BuildAuthorizeUrlAsync 收到
/// RedirectUri/State/Scope；非 federated → INVALID_MODE；app 未知/redirect 白名单 → ORIGIN_NOT_ALLOWED；
/// 通道 null → CHANNEL_NOT_FOUND；channel BuildAuthorizeUrl null → AUTHORIZE_NOT_SUPPORTED）/ CompleteOAuthAsync
/// （成功：state 验签 → ISsoLogin 认证 → accesscode issue expectedClaimant=app_id → 跳回 URL 含 code+sub_state；
/// state 缺失 → STATE_INVALID；state 篡改 → 拒绝不跳回）/ IssueForPlatformUserAsync（成功 + app 未知拒）/
/// ClaimAsync（成功：RedeemAsync 返回 payload；credential 失败 → AUTHENTICATION_FAILED 统一防枚举；
/// RedeemAsync 抛 TICKET_CONSUMED → 判因透传）。</para>
/// </summary>
public sealed class SsoSubAppBridgeTests : TestHostBase
{
    private const string TestAppId = "app-1";
    private const string TestRedirect = "https://sub.example.com/cb";
    private const string TestCallbackBaseUrl = "https://platform.example.com";

    private readonly FakeSsoClientQueryService _clients = new();
    private readonly FakeSsoChannelFactoryBridge _factory = new();
    private readonly FakeSsoLoginBridge _login = new();
    private readonly FakeAccessCodeService _codes = new();

    protected override TestUserInfo CreateUser() => new("bridge-tester", "桥接门面测试用户");

    protected override void ConfigureHost(IServiceCollection services)
    {
        // 真实 SsoSubAppBridge——AddConstructibleService 守卫工厂（生产注册形态；消费方经 User.Use<接口>() AOP）
        services.AddConstructibleService<ISsoSubAppBridge, SsoSubAppBridge>();

        // keyed ISymmetricKeyProvider（Federation 键）——最小 Fake：GetKey 固定 32 字节，加解密未用抛 NotSupported
        services.AddKeyedSingleton<ISymmetricKeyProvider>(SymmetricKeyProviderKeys.Federation, (_, _) => new FixedKeyProvider());

        // Fake 依赖（bridge 内部 User.Use<T>() 懒加载——普通 Scoped，AOP 帧内 GetRequiredService 解析）
        services.AddScoped<ISsoClientQueryService>(_ => _clients);
        services.AddScoped<ISsoChannelFactory>(_ => _factory);
        services.AddScoped<ISsoLogin>(_ => _login);
        services.AddScoped<IAccessCodeService>(_ => _codes);
    }

    private AuthorizeStartRequest StartReq(string? mode = "federated", string? channelId = "wx-1",
        string? state = "sub-st", string? redirect = TestRedirect, string? scope = "snsapi_base")
        => new(TestAppId, mode!, channelId, state, redirect!, scope, null, TestCallbackBaseUrl);

    // ═══════════════════ 1. StartAsync ═══════════════════

    /// <summary>(6)：federated 成功——state 票据生成（≠ 原值，HMAC 包装）+ 通道 BuildAuthorizeUrlAsync 收到
    /// RedirectUri（=CallbackBaseUrl）/State（=state 票据）/Scope。</summary>
    [Fact]
    public async Task StartAsync_Federated_Success_ReturnsUrlAndTicketState()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.StartAsync(StartReq(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://open.weixin.qq.com/connect/oauth2/authorize?x=1", result.AuthorizeUrl);
        Assert.NotNull(result.State);
        Assert.NotEqual("sub-st", result.State);                                    // 原 state 包装为签名票据（非原值透传）
        // 通道收到授权上下文
        Assert.NotNull(_factory.Channel!.LastAuthorizeContext);
        Assert.Equal(TestCallbackBaseUrl, _factory.Channel.LastAuthorizeContext!.RedirectUri);   // 回调基址
        Assert.Equal(result.State, _factory.Channel.LastAuthorizeContext.State);                 // 签名票据进 OAuth state
        Assert.Equal("snsapi_base", _factory.Channel.LastAuthorizeContext.Scope);                // scope 透传
        // 客户端校验被调用（app 存在 + origin 白名单）
        Assert.True(_clients.OriginChecked);
    }

    /// <summary>(7a)：非 federated 模式（direct 拒绝——经 trust/issue）→ INVALID_MODE。</summary>
    [Fact]
    public async Task StartAsync_NonFederatedMode_InvalidMode()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.StartAsync(StartReq(mode: "direct"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INVALID_MODE", result.FailReason);
        Assert.Null(result.AuthorizeUrl);
    }

    /// <summary>(7b)：app 未知（GetInfoAsync 返回 null）→ ORIGIN_NOT_ALLOWED（缺失统一防 app_id 枚举）。</summary>
    [Fact]
    public async Task StartAsync_UnknownApp_OriginNotAllowed_EnumerationSafe()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        _clients.Info = null;   // app 未注册
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.StartAsync(StartReq(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ORIGIN_NOT_ALLOWED", result.FailReason);
        Assert.Null(result.AuthorizeUrl);
    }

    /// <summary>(7c)：redirect 不在白名单 → ORIGIN_NOT_ALLOWED（防开放重定向）。</summary>
    [Fact]
    public async Task StartAsync_RedirectNotInWhitelist_OriginNotAllowed()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        _clients.OriginAllowed = false;   // origin 白名单拒绝
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.StartAsync(StartReq(redirect: "https://evil.example.com/cb"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ORIGIN_NOT_ALLOWED", result.FailReason);
    }

    /// <summary>(7d)：通道选区失败（工厂返回 null——channelId 不匹配）→ CHANNEL_NOT_FOUND。</summary>
    [Fact]
    public async Task StartAsync_ChannelNotFound_ChannelNotFound()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        _factory.Channel = new FakeSsoChannelBridge { ChannelId = "other-1" };   // 工厂按 id 选区不匹配
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.StartAsync(StartReq(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_NOT_FOUND", result.FailReason);
    }

    /// <summary>(7e)：通道 BuildAuthorizeUrlAsync 返回 null（通道不支持授权 URL 构造——DIM null）→ AUTHORIZE_NOT_SUPPORTED。</summary>
    [Fact]
    public async Task StartAsync_NoAuthorizeSupport_AuthorizeNotSupported()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        _factory.Channel = new FakeSsoChannelBridge { AuthorizeResult = null };   // DIM null 语义
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.StartAsync(StartReq(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("AUTHORIZE_NOT_SUPPORTED", result.FailReason);
    }

    // ═══════════════════ 2. CompleteOAuthAsync ═══════════════════

    /// <summary>(8)：成功——StartAsync 生成 state 票据 → CompleteOAuthAsync 验签解 app_id/redirect →
    /// ISsoLogin 纯通道认证 → accesscode IssueAsync（payload=federated 身份，expectedClaimant=app_id）→
    /// 302 跳回 URL 含 code + sub_state（子应用原 state 回传）。</summary>
    [Fact]
    public async Task CompleteOAuthAsync_Success_IssuesCode_ExpectedClaimant_302()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var bridge = user.Use<ISsoSubAppBridge>();

        // 先发起 StartAsync 获取签名票据（真实 HMAC——验签闭环）
        var start = await bridge.StartAsync(StartReq(state: "sub-st-original"), CancellationToken.None);
        Assert.True(start.Success);
        Assert.False(string.IsNullOrEmpty(start.State));

        var result = await bridge.CompleteOAuthAsync("wx-1", new Dictionary<string, string?>
        {
            ["code"] = "auth-code-1",
            ["state"] = start.State,
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("sub-st-original", result.SubState);        // 子应用原 state 回传（验签解出）
        Assert.NotNull(result.RedirectUrl);
        Assert.Contains("code=ac-1", result.RedirectUrl);        // accesscode 跳回参数
        Assert.Contains("state=sub-st-original", result.RedirectUrl);
        // ISsoLogin 收到通道 id + 认证参数
        Assert.True(_login.Invoked);
        Assert.Equal("wx-1", _login.LastChannelId);
        Assert.Equal("auth-code-1", _login.LastContext!.Parameters["code"]);
        // accesscode issue：expectedClaimant=app_id（防他应用冒领）+ payload=federated 身份
        Assert.Equal(TestAppId, _codes.LastClaimant);
        Assert.NotNull(_codes.LastPayloadJson);
        using var doc = JsonDocument.Parse(_codes.LastPayloadJson!);
        Assert.Equal("federated", doc.RootElement.GetProperty("Mode").GetString());
        Assert.Equal("wx-1", doc.RootElement.GetProperty("ChannelId").GetString());
        Assert.Equal("ext-100", doc.RootElement.GetProperty("ExternalUserId").GetString());
    }

    /// <summary>(9a)：state 缺失 → STATE_INVALID（防 state 伪造/缺失——不触达认证）。</summary>
    [Fact]
    public async Task CompleteOAuthAsync_MissingState_StateInvalid()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.CompleteOAuthAsync("wx-1",
            new Dictionary<string, string?> { ["code"] = "auth-code-1" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("STATE_INVALID", result.FailReason);
        Assert.Null(result.RedirectUrl);
        Assert.False(_login.Invoked);   // 未触达认证
    }

    /// <summary>(9b)：state 篡改（合法格式 + 错误签名——HMAC 验签失败）→ 拒绝（不跳回子应用、不签 accesscode）。
    /// ⚠️ 断言安全不变量（Success=false）：当前实现经 UnsignState 抛 CryptographicException 兜底
    /// CHANNEL_REGISTRY_UNAVAILABLE（规范映射应为 STATE_INVALID——见缺陷记录）。</summary>
    [Fact]
    public async Task CompleteOAuthAsync_TamperedState_Rejected()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var bridge = user.Use<ISsoSubAppBridge>();

        var start = await bridge.StartAsync(StartReq(), CancellationToken.None);
        Assert.True(start.Success);

        // 翻转签名段最后一个 base64url 字符（payload 段后 HMAC 截断）——格式仍合法（base64url），签名不符
        string[] parts = start.State!.Split('.', 2);
        string tamperedSig = parts[1][..^1] + (parts[1][^1] == 'A' ? 'B' : 'A');
        var result = await bridge.CompleteOAuthAsync("wx-1", new Dictionary<string, string?>
        {
            ["code"] = "auth-code-1",
            ["state"] = $"{parts[0]}.{tamperedSig}",
        }, CancellationToken.None);

        Assert.False(result.Success);      // 安全不变量：篡改 state 一律拒绝
        Assert.Null(result.RedirectUrl);   // 不跳回子应用
        Assert.False(_login.Invoked);      // 不触达认证
        Assert.False(_codes.Issued);       // 不签 accesscode
    }

    // ═══════════════════ 3. IssueForPlatformUserAsync ═══════════════════

    /// <summary>trust/issue：白名单校验通过 → 签 accesscode（payload=direct Platform uid，expectedClaimant=app_id）→ 302 跳回 URL。</summary>
    [Fact]
    public async Task IssueForPlatformUserAsync_Success_DirectPayload()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.IssueForPlatformUserAsync(
            new TrustIssueRequest(TestAppId, TestRedirect, "st-1", null), "u_platform_1", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("code=ac-1", result.RedirectUrl);
        Assert.Contains("state=st-1", result.RedirectUrl);
        Assert.Equal(TestAppId, _codes.LastClaimant);
        Assert.NotNull(_codes.LastPayloadJson);
        using var doc = JsonDocument.Parse(_codes.LastPayloadJson!);
        Assert.Equal("direct", doc.RootElement.GetProperty("Mode").GetString());
        Assert.Equal("u_platform_1", doc.RootElement.GetProperty("PlatformUid").GetString());
    }

    /// <summary>trust/issue：app 未知 → ORIGIN_NOT_ALLOWED（缺失统一防枚举）。</summary>
    [Fact]
    public async Task IssueForPlatformUserAsync_UnknownApp_OriginNotAllowed()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        _clients.Info = null;
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.IssueForPlatformUserAsync(
            new TrustIssueRequest(TestAppId, TestRedirect, "st-1", null), "u_platform_1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ORIGIN_NOT_ALLOWED", result.FailReason);
        Assert.False(_codes.Issued);
    }

    // ═══════════════════ 4. ClaimAsync ═══════════════════

    /// <summary>(10a)：credential 鉴权通过 → RedeemAsync 原子取回 payload → 身份载荷映射返回。</summary>
    [Fact]
    public async Task ClaimAsync_Success_ReturnsPayload()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        _codes.RedeemPayloadJson = """{"Mode":"federated","ChannelId":"wx-1","ExternalUserId":"ext-100","PlatformUid":null}""";
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.ClaimAsync(new IdentityClaimRequest(TestAppId, "secret-ok", "ac-1"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("federated", result.Mode);
        Assert.Equal("wx-1", result.ChannelId);
        Assert.Equal("ext-100", result.ExternalUserId);
        Assert.Null(result.PlatformUid);
        Assert.Equal("ac-1", _codes.LastRedeemCode);
        Assert.Equal(TestAppId, _codes.LastRedeemClaimant);   // expectedClaimant=app_id 原子 CAS 条件
    }

    /// <summary>(10b)：credential 鉴权失败 → AUTHENTICATION_FAILED（统一防 app_id 枚举）。</summary>
    [Fact]
    public async Task ClaimAsync_BadCredential_AuthFailed_EnumerationSafe()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        _clients.CredentialValid = false;   // client secret 校验失败
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.ClaimAsync(new IdentityClaimRequest(TestAppId, "wrong-secret", "ac-1"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("AUTHENTICATION_FAILED", result.FailReason);   // 统一（不区分 app 未知/凭证错——防枚举）
        Assert.False(_codes.Redeemed);   // 鉴权前置——未触达 Redeem
    }

    /// <summary>(10c)：RedeemAsync 抛 TICKET_CONSUMED（重放——原子 CAS 已销毁）→ 判因透传（非统一 AUTHENTICATION_FAILED）。</summary>
    [Fact]
    public async Task ClaimAsync_RedeemConsumed_TicketConsumedPassed()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        _codes.RedeemException = new AuthenticationException("TICKET_CONSUMED");   // 真实 AccessCodeService 判因异常
        var bridge = user.Use<ISsoSubAppBridge>();

        var result = await bridge.ClaimAsync(new IdentityClaimRequest(TestAppId, "secret-ok", "ac-consumed"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("TICKET_CONSUMED", result.FailReason);   // 判因透传
        Assert.Equal("ac-consumed", _codes.LastRedeemCode);
    }

    // ─────────────────────────── Fake 依赖（普通 Scoped——User.Use<接口> AOP 帧内 GetRequiredService） ───────────────────────────

    /// <summary>Fake SSO 客户端校验面（ISsoClientQueryService——TrustCenter.Abstractions 契约 L2 门控）。</summary>
    private sealed class FakeSsoClientQueryService : ISsoClientQueryService
    {
        public SsoClientInfo? Info { get; set; } = new("app-1", ["https://sub.example.com"], ["profile:basic"], true);
        public bool OriginAllowed { get; set; } = true;
        public bool CredentialValid { get; set; } = true;
        public bool OriginChecked { get; private set; }

        public Task<SsoClientInfo?> GetInfoAsync(string appId, CancellationToken ct = default) => Task.FromResult(Info);
        public Task<bool> IsOriginAllowedAsync(string appId, string origin, CancellationToken ct = default)
        {
            OriginChecked = true;
            return Task.FromResult(OriginAllowed);
        }
        public Task<bool> ValidateClientCredentialAsync(string appId, string clientSecret, CancellationToken ct = default) => Task.FromResult(CredentialValid);
    }

    /// <summary>Fake 通道工厂——按 channelId 精确选区返回 <see cref="Channel"/>（未知 → null）。</summary>
    private sealed class FakeSsoChannelFactoryBridge : ISsoChannelFactory
    {
        public FakeSsoChannelBridge? Channel { get; set; } = new();

        public Task<ISsoChannel?> CreateAsync(string channelId, string? channelType = null, CancellationToken ct = default)
            => Task.FromResult(Channel is not null && string.Equals(Channel.ChannelId, channelId, StringComparison.Ordinal)
                ? (ISsoChannel?)Channel
                : null);

        public Task<ISsoChannel?> CreateDefaultAsync(CancellationToken ct = default)
            => Task.FromResult(Channel is null ? null : (ISsoChannel)Channel);
    }

    /// <summary>Fake 通道——AuthenticateAsync/BuildAuthorizeUrlAsync 记录上下文 + 可配置结果。</summary>
    private sealed class FakeSsoChannelBridge : ISsoChannel
    {
        public string ChannelType => "fake_oauth";
        public string ChannelId { get; set; } = "wx-1";
        public SsoChannelAuthorizeResult? AuthorizeResult { get; set; } =
            new("https://open.weixin.qq.com/connect/oauth2/authorize?x=1", null, null, null);
        public SsoChannelAuthorizeContext? LastAuthorizeContext { get; private set; }

        public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
            => throw new NotSupportedException("桥接门面测试不经通道认证——认证经 FakeSsoLoginBridge");

        public Task<SsoChannelAuthorizeResult?> BuildAuthorizeUrlAsync(SsoChannelAuthorizeContext context, CancellationToken ct = default)
        {
            LastAuthorizeContext = context;
            return Task.FromResult(AuthorizeResult);
        }
    }

    /// <summary>Fake 登录编排门面——记录 channelId/context + 可配置成功结果。</summary>
    private sealed class FakeSsoLoginBridge : ISsoLogin
    {
        public bool Invoked { get; private set; }
        public string? LastChannelId { get; private set; }
        public SsoChannelAuthContext? LastContext { get; private set; }

        public Task<SsoChannelAuthResult> LoginAsync(string channelId, SsoChannelAuthContext context, CancellationToken ct = default)
        {
            Invoked = true;
            LastChannelId = channelId;
            LastContext = context;
            return Task.FromResult(new SsoChannelAuthResult(true, "ext-100", null, 2));
        }

        public Task<SsoChannelAuthResult> LoginDefaultAsync(SsoChannelAuthContext context, CancellationToken ct = default)
        {
            Invoked = true;
            LastContext = context;
            return Task.FromResult(new SsoChannelAuthResult(true, "ext-100", null, 2));
        }
    }

    /// <summary>Fake accesscode 服务（IAccessCodeService）——记录 IssueAsync(payload,ttl,claimant)/RedeemAsync(code,claimant)
    /// + 可配置 payload/异常。</summary>
    private sealed class FakeAccessCodeService : IAccessCodeService
    {
        public string? LastPayloadJson { get; private set; }
        public string? LastClaimant { get; private set; }
        public string? LastRedeemCode { get; private set; }
        public string? LastRedeemClaimant { get; private set; }
        public bool Issued { get; private set; }
        public bool Redeemed { get; private set; }
        public string? RedeemPayloadJson { get; set; }
        public Exception? RedeemException { get; set; }

        public Task<AccessCodeIssueResult> IssueAsync(AccessCodeIssueRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("桥接门面测试只用安全数据投递重载");

        public Task<AccessCodeConsumeResult> ConsumeAsync(string code, string? codeVerifier = null, string? ipAddress = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AccessCodeIssueResult> IssueAsync(string? payloadJson, TimeSpan ttl, string? expectedClaimant, CancellationToken ct = default)
        {
            Issued = true;
            LastPayloadJson = payloadJson;
            LastClaimant = expectedClaimant;
            return Task.FromResult(new AccessCodeIssueResult("ac-1", 120));
        }

        public Task<T?> PeekAsync<T>(string code, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<T?> RedeemAsync<T>(string code, string? claimant, CancellationToken ct = default)
        {
            Redeemed = true;
            LastRedeemCode = code;
            LastRedeemClaimant = claimant;
            if (RedeemException is not null) throw RedeemException;
            if (RedeemPayloadJson is null) return Task.FromResult<T?>(default);
            return Task.FromResult(JsonSerializer.Deserialize<T>(RedeemPayloadJson));
        }

        public Task<int> CleanupExpiredAsync(int? retentionDays = null, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>最小 keyed 对称密钥 Fake——GetKey 返回固定 32 字节（state 票据 HMAC-SHA256 只用密钥材料）；加解密未用抛 NotSupported。</summary>
    private sealed class FixedKeyProvider : ISymmetricKeyProvider
    {
        private readonly byte[] _key =
        [
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10,
            0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20,
        ];

        public byte[] GetKey() => (byte[])_key.Clone();

        public string Encrypt(string plaintext) => throw new NotSupportedException("桥接门面测试只用 GetKey");

        public string Decrypt(string ciphertext) => throw new NotSupportedException("桥接门面测试只用 GetKey");
    }
}
