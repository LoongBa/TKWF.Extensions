using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using Xunit;

namespace TKWF.Federation.{Namespace}.Tests;

/// <summary>
/// <see cref="{Platform}OauthChannel"/>（{platform}_oauth——平台网页授权身份获取）测试——生产路径宿主
/// （真实 DI + <c>Add{Platform}FederationChannels</c> + 探针门面帧内枚举）+ Stub 平台 API（code→external_uid）。
/// </summary>
public class {Platform}OauthChannelTests : {Platform}ChannelTestHost
{
    private const string TestAppId = "test-appid";
    private const string TestExternalUserId = "test_external_uid_1";

    private (DomainUser<TestUserInfo> User, {Platform}OauthChannel Channel) Setup(bool failOauth = false)
    {
        Handler = {Platform}TestHost.CreateStubHandler(TestExternalUserId, failOauth);
        ConfigureOptions = o => o.Channels = [{Platform}TestHost.CreateChannelConfig(appId: TestAppId)];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        var channel = Assert.IsType<{Platform}OauthChannel>(
            Assert.Single(probe.Channels, c => c.ChannelType == "{platform}_oauth"));
        return (user, channel);
    }

    [Fact]
    public void ChannelType_Is{Platform}Oauth_AndChannelIdFromOptions()
    {
        ConfigureOptions = o => o.Channels = [{Platform}TestHost.CreateChannelConfig(channelId: "mp-main", appId: TestAppId)];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        var channel = Assert.Single(probe.Channels, c => c.ChannelType == "{platform}_oauth");
        Assert.Equal("{platform}_oauth", channel.ChannelType);
        Assert.Equal("mp-main", channel.ChannelId); // ChannelId = 配置 channel id（Options.Channels 选区）
    }

    [Fact]
    public async Task AuthenticateAsync_ValidCode_ReturnsExternalId_Success()
    {
        using var _ = this;
        var (_, channel) = Setup();
        var context = {Platform}TestHost.CreateContext(("code", "oauth-code-123"));

        var result = await channel.AuthenticateAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestExternalUserId, result.ExternalUserId); // Stub 返回 external_uid
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel); // 平台便捷
    }

    [Fact]
    public async Task AuthenticateAsync_MissingCode_Fails_{Platform}_CODE_REQUIRED()
    {
        using var _ = this;
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(
            {Platform}TestHost.CreateContext(("state", "abc")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("{Namespace}_CODE_REQUIRED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_ApiError_Fails_WithMessage()
    {
        // 平台 API 返回 errcode（code 非法/已消费）→ 失败 + FailReason 携带错误消息
        using var _ = this;
        var (_, channel) = Setup(failOauth: true);

        var result = await channel.AuthenticateAsync(
            {Platform}TestHost.CreateContext(("code", "stale-code")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailReason);
        Assert.Null(result.ExternalUserId);
    }
}
