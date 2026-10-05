using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using Xunit;

namespace TKWF.Federation.WeChat.Tests;

/// <summary>
/// <see cref="WeChatOauthChannel"/>（wechat_oauth——公众号网页授权身份获取）测试——生产路径宿主
/// （真实 DI + <c>AddWeChatFederationChannels</c> + 探针门面帧内枚举）+ Stub 微信 API（code→openid）。
/// </summary>
public class WeChatOauthChannelTests : WeChatChannelTestHost
{
    private const string TestAppId = "wx0000000000000000";
    private const string TestOpenId = "oa_test_openid_1";

    private (DomainUser<TestUserInfo> User, WeChatOauthChannel Channel) Setup(bool failOauth = false)
    {
        Handler = WeChatTestHost.CreateStubHandler(TestOpenId, failOauth);
        ConfigureOptions = o => o.Channels = [WeChatTestHost.CreateChannelConfig(appId: TestAppId)];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        var channel = Assert.IsType<WeChatOauthChannel>(
            Assert.Single(probe.Channels, c => c.ChannelType == "wechat_oauth"));
        return (user, channel);
    }

    [Fact]
    public void ChannelType_IsWechatOauth_AndChannelIdFromOptions()
    {
        ConfigureOptions = o => o.Channels = [WeChatTestHost.CreateChannelConfig(channelId: "mp-main", appId: TestAppId)];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        var channel = Assert.Single(probe.Channels, c => c.ChannelType == "wechat_oauth");
        Assert.Equal("wechat_oauth", channel.ChannelType);
        Assert.Equal("mp-main", channel.ChannelId); // ChannelId = 配置公众号 id（Options.Channels 选区）
    }

    [Fact]
    public async Task AuthenticateAsync_ValidCode_ReturnsOpenId_Success()
    {
        using var _ = this;
        var (_, channel) = Setup();
        var context = WeChatTestHost.CreateContext(("code", "oauth-code-123"));

        var result = await channel.AuthenticateAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenId, result.ExternalUserId); // Stub 返回 openid
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel); // 微信便捷
    }

    [Fact]
    public async Task AuthenticateAsync_MissingCode_Fails_WECHAT_CODE_REQUIRED()
    {
        using var _ = this;
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(("state", "abc")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECHAT_CODE_REQUIRED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_ApiError_Fails_WithMessage()
    {
        // 微信 API 返回 errcode（code 非法/已消费）→ 失败 + FailReason 携带错误消息
        using var _ = this;
        var (_, channel) = Setup(failOauth: true);

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(("code", "stale-code")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailReason);
        Assert.Null(result.ExternalUserId);
    }
}
