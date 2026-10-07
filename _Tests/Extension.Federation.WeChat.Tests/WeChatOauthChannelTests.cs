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
    private const string TestChannelId = "mp-main";

    private async Task<(DomainUser<TestUserInfo> User, WeChatOauthChannel Channel)> Setup(bool failOauth = false)
    {
        Handler = WeChatTestHost.CreateStubHandler(TestOpenId, failOauth);
        ConfigureOptions = o => o.Channels = [WeChatTestHost.CreateChannelConfig(channelId: TestChannelId, appId: TestAppId)];
        var (_, user) = BindTestScope();
        // v0.3.0：经 ISsoChannelFactory 按 channelId 构造真实实例（凭证从 registry 懒加载）
        var channel = await user.Use<ISsoChannelFactory>().CreateAsync(TestChannelId, "wechat_oauth");
        return (user, Assert.IsType<WeChatOauthChannel>(channel));
    }

    [Fact]
    public async Task ChannelType_IsWechatOauth_AndChannelId_FromFactory()
    {
        ConfigureOptions = o => o.Channels = [WeChatTestHost.CreateChannelConfig(channelId: TestChannelId, appId: TestAppId)];
        var (_, user) = BindTestScope();
        // 探针枚举集合（模板实例——ChannelType 标识注册）……
        var probe = user.Use<IChannelProbe>();
        var template = Assert.Single(probe.Channels, c => c.ChannelType == "wechat_oauth");
        Assert.Equal("wechat_oauth", template.ChannelType);
        // ……真实实例经工厂构造（ChannelId = 注册表选区键）
        var channel = await user.Use<ISsoChannelFactory>().CreateAsync(TestChannelId, "wechat_oauth");
        Assert.NotNull(channel);
        Assert.Equal(TestChannelId, channel!.ChannelId);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidCode_ReturnsOpenId_Success()
    {
        using var _ = this;
        var (_, channel) = await Setup();
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
        var (_, channel) = await Setup();

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
        var (_, channel) = await Setup(failOauth: true);

        var result = await channel.AuthenticateAsync(
            WeChatTestHost.CreateContext(("code", "stale-code")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailReason);
        Assert.Null(result.ExternalUserId);
    }
}
