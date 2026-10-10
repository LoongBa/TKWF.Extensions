using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.TrustCenter;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// AuthCenter 归层 T6 装配链路确认——Federation 扩展（外交部）经 <see cref="ISsoChannel"/> 集合窄适配编排平台网关库。
/// <para><b>核查结论</b>：<see cref="FederationExtensionInitializer{TUserInfo}"/> 自身<b>零 ISsoChannel 注册</b>——
/// Federation 无通道实现，且 <c>TryAddEnumerableConstructible&lt;TInterface, TImpl&gt;</c> 要求
/// <c>TImpl : class, TInterface</c> 且 ≠（无可注册实现）。集合注册责任归平台网关库扩展方法
/// <c>AddWeChatFederationChannels()</c>（内部 <c>TryAddEnumerableConstructible&lt;ISsoChannel, X&gt;()</c>——
/// 归层实施方案 §5.4 Oracle P1-4/评审点 7）。</para>
/// <para>装配语义验证：未装配库时 <c>IEnumerable&lt;ISsoChannel&gt;</c> 帧内解析为空（"未注册通道自然跳过" F5）；
/// 库式追加（TryAddEnumerableConstructible）后集合元素经 ADR92 守卫工厂帧内供给；帧外枚举抛守卫（禁止形态）。</para>
/// </summary>
public class SsoChannelCollectionTests : TestHostBase
{
    protected override TestUserInfo CreateUser() => new("channel-t6", "通道集合测试用户");

    protected override void ConfigureHost(IServiceCollection services)
    {
        // Federation 初始器（门面 + Options；零 ISsoChannel 注册——核查目标）
        new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 测试门面：ctor 注入 IEnumerable<ISsoChannel>（编排门面同形——ADR92 集合消费姿势）
        services.AddConstructibleService<ISsoChannelSet, SsoChannelSet>();
    }

    /// <summary>核查：Federation 初始器自身零 ISsoChannel 描述符——集合注册责任归库扩展方法（设计正解）。</summary>
    [Fact]
    public void FederationInitializer_RegistersZeroISsoChannelElements_ByDesign()
    {
        var services = new ServiceCollection();
        new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(ISsoChannel)).ToList();
        Assert.Empty(descriptors);
    }

    /// <summary>未装配平台网关库 → 帧内枚举空集合（"未注册通道自然跳过" F5）。</summary>
    [Fact]
    public void Unassembled_InFrame_CollectionEmpty()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        Assert.Equal(0, user.Use<ISsoChannelSet>().Count);
    }

    /// <summary>库式追加（模拟 AddWeChatFederationChannels 内部 TryAddEnumerableConstructible×2）→ 帧内集合 2 元素。</summary>
    [Fact]
    public void LibraryStyleTryAddEnumerableConstructible_InFrame_CollectionWorks()
    {
        using var _ = this;
        // configure 在 ConfigureHost 之后执行——对齐库扩展方法装配时序（消费方 ConfigureServices 调用）
        var (_, user) = BindTestScope(services =>
        {
            services.TryAddEnumerableConstructible<ISsoChannel, StubOauthChannel>();
            services.TryAddEnumerableConstructible<ISsoChannel, StubEventChannel>();
        });

        var set = user.Use<ISsoChannelSet>();
        Assert.Equal(2, set.Count);
        Assert.Equal(["wechat_oauth", "wechat_event"], set.ChannelTypes);
    }

    /// <summary>帧外 `GetServices&lt;ISsoChannel&gt;()` → 守卫工厂抛（禁止形态——控制器不得 [FromServices] 预绑定，对齐 ADR92）。</summary>
    [Fact]
    public void FrameOut_GetServices_ThrowsGuard()
    {
        using var _ = this;
        var (provider, _) = BindTestScope(services =>
            services.TryAddEnumerableConstructible<ISsoChannel, StubOauthChannel>());

        using var scope = provider.CreateScope();
        var ex = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetServices<ISsoChannel>().ToArray());
        Assert.Contains("域作用域帧外被枚举构造", ex.Message);
    }

    // ── 测试门面 + 桩通道（仅测试项目——模拟平台网关库通道形态，不改库代码） ──

    /// <summary>编排门面同形：ctor 注入集合，帧内数组化（对齐 ITestProviderSet/FeatureManager 消费姿势）。</summary>
    private interface ISsoChannelSet : IDomainService
    {
        int Count { get; }
        string[] ChannelTypes { get; }
    }

    private sealed class SsoChannelSet(IDomainUser user, IEnumerable<ISsoChannel> channels)
        : DomainServiceBase(user), ISsoChannelSet
    {
        private readonly ISsoChannel[] _channels = channels.ToArray();
        public int Count => _channels.Length;
        public string[] ChannelTypes => _channels.Select(c => c.ChannelType).ToArray();
    }

    /// <summary>桩 OAuth 通道（wechat_oauth 同形——形态对齐 WeChatOauthChannel）。</summary>
    private sealed class StubOauthChannel(IDomainUser user) : DomainServiceBase(user), ISsoChannel
    {
        public string ChannelType => "wechat_oauth";
        public string ChannelId => "mp-stub";
        public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
            => Task.FromResult(new SsoChannelAuthResult(true, "openid-stub", null, 2));
    }

    /// <summary>桩事件通道（wechat_event 同形——形态对齐 WeChatEventChannel）。</summary>
    private sealed class StubEventChannel(IDomainUser user) : DomainServiceBase(user), ISsoChannel
    {
        public string ChannelType => "wechat_event";
        public string ChannelId => "mp-stub";
        public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
            => Task.FromResult(new SsoChannelAuthResult(true, "openid-stub", null, 2));
    }
}