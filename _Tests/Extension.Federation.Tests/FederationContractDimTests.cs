using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// I2 契约 DIM 默认值断言（方案 §10 测试矩阵——I2-2 验收：传输/语义层新增契约的 fail-closed 默认值必须显式锁定）。
/// <para>覆盖：① <see cref="IChannelSource.Capabilities"/> 默认 <see cref="ChannelCapabilities.None"/>（新增平台库忘记声明 →
/// 关注域静默不记录——刻意 fail-closed，方案 §3.4）；② <see cref="ISsoChannel.HandleEventAsync"/> 默认
/// <see cref="ChannelEventOutcome.NotApplicable"/>（非事件通道零强制——DIM，方案 §3.3）；③
/// <see cref="ISsoChannel.VerifyUrlAsync"/> 默认 null / <see cref="ISsoChannel.BuildAuthorizeUrlAsync"/> 默认 null
/// （既有 DIM 锚定——防未来实现误破坏）。</para>
/// </summary>
public sealed class FederationContractDimTests
{
    /// <summary>IChannelSource 默认 Capabilities == None（新增平台库零声明 → fail-closed 不记录关注域）。</summary>
    [Fact]
    public void ChannelSource_DefaultCapabilities_IsNone()
    {
        IChannelSource source = new DefaultChannelSource();   // 接口引用（DIM 成员须经接口调用）
        Assert.Equal(ChannelCapabilities.None, source.Capabilities);
    }

    /// <summary>ISsoChannel.HandleEventAsync DIM 默认 NotApplicable（非事件通道 → 编排跳过——方案 §3.3 DIM 模式）。</summary>
    [Fact]
    public async Task SsoChannel_DefaultHandleEvent_IsNotApplicable()
    {
        ISsoChannel channel = new DefaultChannel();   // 接口引用（DIM 成员须经接口调用）
        var outcome = await channel.HandleEventAsync(new ChannelInboundContext(new Dictionary<string, string?>()));
        Assert.Equal(ChannelEventOutcomeState.NotApplicable, outcome.State);
        Assert.Null(outcome.Event);
    }

    /// <summary>ISsoChannel.VerifyUrlAsync DIM 默认 null（无 URL 验证契约 → 装配层映射 400——既有语义锚定）。</summary>
    [Fact]
    public async Task SsoChannel_DefaultVerifyUrl_IsNull()
    {
        ISsoChannel channel = new DefaultChannel();   // 接口引用（DIM 成员须经接口调用）
        var echo = await channel.VerifyUrlAsync(new ChannelInboundContext(new Dictionary<string, string?>()));
        Assert.Null(echo);
    }

    /// <summary>ISsoChannel.BuildAuthorizeUrlAsync DIM 默认 null（通道不支持授权 URL → 端点 AUTHORIZE_NOT_SUPPORTED）。</summary>
    [Fact]
    public async Task SsoChannel_DefaultBuildAuthorizeUrl_IsNull()
    {
        ISsoChannel channel = new DefaultChannel();   // 接口引用（DIM 成员须经接口调用）
        var url = await channel.BuildAuthorizeUrlAsync(
            new SsoChannelAuthorizeContext("https://cb.example.com", null, null, new Dictionary<string, string?>()));
        Assert.Null(url);
    }

    /// <summary>DIM 默认实现的最小覆盖通道（零成员覆写——验证契约 DIM 缺省值）。</summary>
    private sealed class DefaultChannel : ISsoChannel
    {
        public string ChannelType => "test_default";
        public string ChannelId => "test-default";
        public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
            => Task.FromResult(new SsoChannelAuthResult(false, null, "NOT_IMPLEMENTED", 0));
    }

    private sealed class DefaultChannelSource : IChannelSource
    {
        public string PlatformType => "test";
        public IReadOnlyList<ChannelConfig> GetChannels() => [];
    }
}