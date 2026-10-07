using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Federation.Oidc.Tests;

/// <summary>
/// 派生通道测试（<c>AddOidcDerivedChannels&lt;TChannel&gt;</c>——M2 Google/Microsoft 形态预验证）：
/// <para>覆盖：① pairwise 复合编码断言（F3——channel_id = <c>$"{ChannelType}:{{client_id}}"</c>，
/// 同用户不同 client_id → 不同 channel_id 不误并——N2 §3.3 定案）；② 派生通道注册进集合（F4）；
/// ③ 多 IdP 同时装配枚举（F6——派生 + 直配并存集合正确枚举——Oracle P2-7）。</para>
/// </summary>
public class OidcDerivedChannelTests : DerivedChannelTestHost
{
    private const string Issuer = "https://stub.example.com";
    private const string Kid = "test-kid";

    private RSA _privateKey = RSA.Create(2048);
    private StubOidcHandler.OidcStubOptions _stub = new(Issuer, "derived-client", "oidc_stub_sub");

    private async Task<(DomainUser<TestUserInfo> User, ISsoChannel Channel)> SetupDerived()
    {
        TestOidcHandler = new StubOidcHandler(_privateKey, Kid, () => _stub);
        var (_, user) = BindScope();
        // v0.3.0：经 ISsoChannelFactory 按 channelId 构造真实实例（凭证从 registry 懒加载）
        var channel = await user.Use<ISsoChannelFactory>().CreateAsync("derived-1", "derived_oidc");
        return (user, Assert.IsType<TestDerivedChannel>(channel));
    }

    // ---- F3：pairwise 复合编码断言（N2 §3.3 定案：channel_id = {platform}_oidc:{client_id}）----

    [Fact]
    public async Task ChannelId_PairwiseComposite_ContainsClientId()
    {
        ClientId = "microsoft-app-1";   // 模拟 Microsoft App（pairwise sub 按 client_id 派生）
        var (_, channel) = await SetupDerived();

        Assert.Equal("derived_oidc:microsoft-app-1", channel.ChannelId);
    }

    [Fact]
    public async Task SameUser_DifferentClientId_DifferentChannelId_NoMerge()
    {
        ClientId = "app-a";
        var (_, channelA) = await SetupDerived();
        Assert.Equal("derived_oidc:app-a", channelA.ChannelId);

        ClientId = "app-b";   // 同用户不同 App（pairwise 跨 App sub 不同→不同 channel_id）
        var (_, channelB) = await SetupDerived();
        Assert.Equal("derived_oidc:app-b", channelB.ChannelId);

        // channel_id 不同 → 映射表 (channel_id, sub) 不同行 → 不误并（N2 §3.2：各自映射，绑定并合经用户验证）
        Assert.NotEqual(channelA.ChannelId, channelB.ChannelId);
    }

    // ---- F6：多 IdP 同时装配枚举（派生 + 直配并存——Oracle P2-7）----

    [Fact]
    public void MultipleIdPs_EnumerateAllRegisteredChannels()
    {
        // 派生通道（本类） + 直配通道（AddOidcFederationChannel 追加——同容器并存）
        ExtraConfigurator = s => s.AddOidcFederationChannel("keycloak-corp", o =>
        {
            o.Platform = "keycloak";
            o.ChannelId = "keycloak-corp";
            o.ClientId = "kc-client";
            o.ClientSecret = "kc-secret";
            o.AuthorizeUri = "https://stub.example.com/authorize";
            o.TokenUri = "https://stub.example.com/token";
            o.JwksUri = "https://stub.example.com/jwks";
            o.Scopes = ["openid"];
            o.TokenIssuers = [Issuer];
        });

        var (_, user) = BindScope();
        var probe = user.Use<IOidcChannelProbe>();

        Assert.Contains(probe.Channels, c => c.ChannelType == "derived_oidc");     // 派生通道在集合
        Assert.Contains(probe.Channels, c => c.ChannelType == "oidc_oidc");        // 直配通道在集合（稳定类型）
        Assert.Equal(2, probe.Channels.Count);                                     // 两通道并存枚举
    }
}

/// <summary>
/// 派生通道测试宿主——真实 DI（<c>AddOidcDerivedChannels</c> + 测试派生通道来源 + 多通道联邦核心门面 +
/// 探针门面 + Stub HttpClientFactory），ClientId 可配（pairwise 复合编码断言前提）；ExtraConfigurator
/// 支持追加注册（多 IdP 枚举）。
/// </summary>
public class DerivedChannelTestHost : TestHostBase
{
    /// <summary>派生通道 ClientId（pairwise 复合编码 `derived_oidc:{client_id}`）。</summary>
    public string ClientId { get; set; } = "derived-client";

    /// <summary>追加注册（多 IdP 枚举场景——同容器并存派生 + 直配）。</summary>
    public Action<IServiceCollection>? ExtraConfigurator { get; set; }

    /// <summary>OIDC IdP 桩 handler（测试设置）。</summary>
    public HttpMessageHandler TestOidcHandler { get; set; } =
        new StubOidcHandler(RSA.Create(2048), "test-kid",
            () => new StubOidcHandler.OidcStubOptions("https://stub.example.com", "derived-client", "oidc_stub_sub"));

    /// <summary>绑定域作用域（public 包装）。</summary>
    public (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindScope()
        => BindTestScope();

    protected override void ConfigureHost(IServiceCollection services)
    {
        services.AddSingleton<IOptions<TestDerivedOptions>>(_ => Options.Create(
            new TestDerivedOptions { Channels = [new TestDerivedConfig { ChannelId = "derived-1", ClientId = ClientId }] }));
        services.AddOidcDerivedChannels<TestDerivedChannel>();
        // 测试派生通道静态来源（TestDerivedOptions.Channels 投影——registry 聚合选区）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChannelSource, TestDerivedChannelSource>());
        ExtraConfigurator?.Invoke(services);

        // 多通道联邦核心门面（v0.3.0）——不启动 FederationExtensionInitializer 的最小生产等价面
        services.AddOptions<FederationStaticChannelOptions>();
        services.AddScoped<StaticChannelRegistry>();
        services.AddConstructibleService<IChannelRegistry, CompositeChannelRegistry>();
        services.AddConstructibleService<ISsoChannelFactory, SsoChannelFactory>();
        services.AddConstructibleService<ISsoLogin, SsoLogin>();

        services.AddConstructibleService<IOidcChannelProbe, OidcChannelProbe>();
        services.AddSingleton<IHttpClientFactory>(_ => new StubHttpClientFactory(TestOidcHandler));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }
}
