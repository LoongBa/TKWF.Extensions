using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// 外部 IdP 借道验证桥接测试（三层边界 2026-10-09，方案 §5.4 T5）——AuthCenter 经
/// <see cref="IExternalIdpAuthenticator"/> 契约借道 Federation 连接层认证，桥接<b>委托平台库通道非双实现</b>。
/// <para>生产路径宿主模式（对齐 SsoChannelCollectionTests）：真实 DI（FederationExtensionInitializer.ConfigureServices
/// + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> + <c>User.Use&lt;IExternalIdpAuthenticator&gt;()</c>
/// AOP 路径；Fake 通道经 <c>TryAddEnumerableConstructible&lt;ISsoChannel, FakeSsoChannel&gt;()</c> 库式追加
/// （对齐平台网关库 <c>AddXxxFederationChannels()</c> 装配形态）；静态通道来源 stub 供注册表选区（Composite
/// 无 Db 注册 → 回退静态，SsoChannelRegistryTests.StaticOnlyHost 同形）。</para>
/// <para>用例覆盖：显式 <c>channel_id</c> 选区委托成功 / 缺省默认通道降级 / 未注册通道 CHANNEL_NOT_FOUND /
/// 未知 channel_id CHANNEL_NOT_FOUND / parameters 原样透传进 <see cref="SsoChannelAuthContext"/>。</para>
/// </summary>
public class ExternalIdpAuthenticatorTests : TestHostBase
{
    protected override TestUserInfo CreateUser() => new("external-idp-user", "借道验证测试用户");

    protected override void ConfigureHost(IServiceCollection services)
    {
        // Federation 初始器（桥接门面 + 通道工厂 + 注册表门面 + keyed 对称密钥 + Options 兜底）
        new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 静态通道来源（registry 选区数据源——Composite 无 Db 注册回退静态）：
        // fake-default（IsDefault 默认通道——缺省 channel_id 降级）/ fake-mp（显式选区）
        services.AddSingleton<IChannelSource>(new StubStaticSource(
            new ChannelConfig
            {
                ChannelId = "fake-default", PlatformType = "wechat", AppId = "wx-fake", AppSecret = "s1",
                IsEnabled = true, IsDefault = true,
            },
            new ChannelConfig
            {
                ChannelId = "fake-mp", PlatformType = "wechat", AppId = "wx-fake", AppSecret = "s2",
                IsEnabled = true,
            }));
    }

    /// <summary>显式 channel_id 选区 → 工厂构造 Fake 通道（ChannelId 传入）→ 委托认证成功 + AuthLevel 透传。</summary>
    [Fact]
    public async Task Authenticate_WithChannelId_DelegatesToRegisteredChannel_Success()
    {
        ResetFakeState();
        using var _ = this;
        var (_, user) = BindTestScope(services =>
            services.TryAddEnumerableConstructible<ISsoChannel, FakeSsoChannel>());

        var bridge = user.Use<IExternalIdpAuthenticator>();
        var result = await bridge.AuthenticateAsync(
            "wechat_oauth",
            new Dictionary<string, string?> { ["channel_id"] = "fake-mp", ["code"] = "wx-code-1" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("openid-fake", result.ExternalUserId);
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel);                      // AuthLevel int 透传
        Assert.Equal("fake-mp", FakeSsoChannel.LastConstructedChannelId);  // 工厂传入显式 ChannelConfig
        Assert.Equal(1, FakeSsoChannel.AuthenticateCalls);
    }

    /// <summary>缺省 channel_id → 默认通道降级（CreateDefaultAsync——对齐 SsoLogin.LoginDefaultAsync 语义）。</summary>
    [Fact]
    public async Task Authenticate_NoChannelId_FallsBackToDefaultChannel_Success()
    {
        ResetFakeState();
        using var _ = this;
        var (_, user) = BindTestScope(services =>
            services.TryAddEnumerableConstructible<ISsoChannel, FakeSsoChannel>());

        var bridge = user.Use<IExternalIdpAuthenticator>();
        var result = await bridge.AuthenticateAsync(
            "wechat_oauth",
            new Dictionary<string, string?> { ["code"] = "wx-code-2" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("openid-fake", result.ExternalUserId);
        Assert.Equal("fake-default", FakeSsoChannel.LastConstructedChannelId);  // 默认通道选区命中
        Assert.Equal(2, result.AuthLevel);
    }

    /// <summary>Fake 通道未注册进 ISsoChannel 集合（registry 有配置但集合空）→ CHANNEL_NOT_FOUND（机器码）。</summary>
    [Fact]
    public async Task Authenticate_ChannelNotRegisteredInCollection_ReturnsChannelNotFound()
    {
        ResetFakeState();
        using var _ = this;
        // 不追加 FakeSsoChannel——集合空（模拟未装配平台库，方案 §5.4 fail 语义）
        var (_, user) = BindTestScope();

        var bridge = user.Use<IExternalIdpAuthenticator>();
        var result = await bridge.AuthenticateAsync(
            "wechat_oauth",
            new Dictionary<string, string?> { ["channel_id"] = "fake-mp", ["code"] = "wx-code-3" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.ExternalUserId);
        Assert.Equal("CHANNEL_NOT_FOUND", result.FailReason);
        Assert.Equal(0, result.AuthLevel);
    }

    /// <summary>未知 channel_id（registry 无该通道配置）→ CHANNEL_NOT_FOUND。</summary>
    [Fact]
    public async Task Authenticate_UnknownChannelId_ReturnsChannelNotFound()
    {
        ResetFakeState();
        using var _ = this;
        var (_, user) = BindTestScope(services =>
            services.TryAddEnumerableConstructible<ISsoChannel, FakeSsoChannel>());

        var bridge = user.Use<IExternalIdpAuthenticator>();
        var result = await bridge.AuthenticateAsync(
            "wechat_oauth",
            new Dictionary<string, string?> { ["channel_id"] = "no-such-channel", ["code"] = "wx-code-4" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_NOT_FOUND", result.FailReason);
        Assert.Equal(0, result.AuthLevel);
        Assert.Equal(0, FakeSsoChannel.AuthenticateCalls);      // 未委托（选区失败）
    }

    /// <summary>parameters 字典原样透传进 SsoChannelAuthContext（桥接不裁剪/不重写——协议字段归平台库通道解析）。</summary>
    [Fact]
    public async Task Authenticate_PassesParametersThroughToChannelContext()
    {
        ResetFakeState();
        using var _ = this;
        var (_, user) = BindTestScope(services =>
            services.TryAddEnumerableConstructible<ISsoChannel, FakeSsoChannel>());

        var bridge = user.Use<IExternalIdpAuthenticator>();
        var parameters = new Dictionary<string, string?>
        {
            ["channel_id"] = "fake-mp",
            ["code"] = "wx-code-5",
            ["state"] = "csrf-token-abc",
            ["scope"] = "snsapi_userinfo",
        };
        await bridge.AuthenticateAsync("wechat_oauth", parameters, CancellationToken.None);

        Assert.NotNull(FakeSsoChannel.LastContext);
        var received = FakeSsoChannel.LastContext!.Parameters;
        Assert.Equal(parameters.Count, received.Count);
        foreach (var (key, value) in parameters)
            Assert.True(received.TryGetValue(key, out var got) && got == value,
                $"参数 {key} 未原样透传（期望 {value}，实际 {got}）");
    }

    // ── 测试桩（仅测试项目——模拟平台网关库通道形态，不改库代码） ──

    /// <summary>桩 OAuth 通道（wechat_oauth 同形——ctor 对齐平台库通道契约 (IDomainUser, ChannelConfig? channel)）。</summary>
    private sealed class FakeSsoChannel : DomainServiceBase, ISsoChannel
    {
        /// <summary>最近一次认证上下文（参数透传断言）。</summary>
        public static SsoChannelAuthContext? LastContext;

        /// <summary>工厂构造实例传入的 ChannelConfig.ChannelId（选区断言）。</summary>
        public static string? LastConstructedChannelId;

        /// <summary>委托认证调用次数。</summary>
        public static int AuthenticateCalls;

        private readonly ChannelConfig? _channel;

        public FakeSsoChannel(IDomainUser user, ChannelConfig? channel = null) : base(user)
        {
            _channel = channel;
            LastConstructedChannelId = channel?.ChannelId;
        }

        public string ChannelType => "wechat_oauth";
        public string ChannelId => _channel?.ChannelId ?? "fake-mp";

        public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
        {
            LastContext = context;
            AuthenticateCalls++;
            return Task.FromResult(new SsoChannelAuthResult(true, "openid-fake", null, 2));
        }
    }

    /// <summary>静态通道来源 stub（IChannelSource——StaticChannelRegistry 聚合投影，SsoChannelRegistryTests 同形）。</summary>
    private sealed class StubStaticSource : IChannelSource
    {
        private readonly ChannelConfig[] _channels;

        public StubStaticSource(params ChannelConfig[] channels) => _channels = channels;

        public string PlatformType => "stub";
        public IReadOnlyList<ChannelConfig> GetChannels() => _channels;
    }

    /// <summary>清空 Fake 静态捕获状态（跨测试隔离——xunit 同类串行，双保险）。</summary>
    private static void ResetFakeState()
    {
        FakeSsoChannel.LastContext = null;
        FakeSsoChannel.LastConstructedChannelId = null;
        FakeSsoChannel.AuthenticateCalls = 0;
    }
}
