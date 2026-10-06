using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using Xunit;

namespace TKWF.Federation.DingTalk.Tests;

/// <summary>
/// <see cref="DingTalkOauthChannel"/>（dingtalk_oauth——钉钉新 OAuth2 网页授权身份获取）测试——生产路径宿主
/// （真实 DI + <c>AddDingTalkFederationChannels</c> + 探针门面帧内枚举）+ Stub 钉钉 API（code→userAccessToken）。
/// <para>验收（M3 方案 F2）：code→openId 正确（external_uid 恒 = openId，P1-4 两步流）；缺 code 拒；
/// API 错误拒；token 端点不接受 redirect_uri（P1-3——库 AuthenticateAsync 不感知 redirect_uri）。</para>
/// </summary>
public class DingTalkOauthChannelTests : DingTalkChannelTestHost
{
    private const string TestAppKey = "dingAppKey000000000000";
    private const string TestOpenId = "oa_test_openid_1";

    private (DomainUser<TestUserInfo> User, DingTalkOauthChannel Channel) Setup(bool failOauth = false, string? openId = TestOpenId)
    {
        Handler = DingTalkTestHost.CreateStubHandler(openId, failOauth: failOauth);
        ConfigureOptions = o => o.Channels = [DingTalkTestHost.CreateChannelConfig(appKey: TestAppKey)];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        var channel = Assert.IsType<DingTalkOauthChannel>(
            Assert.Single(probe.Channels, c => c.ChannelType == "dingtalk_oauth"));
        return (user, channel);
    }

    [Fact]
    public void ChannelType_IsDingtalkOauth_AndChannelIdFromOptions()
    {
        ConfigureOptions = o => o.Channels = [DingTalkTestHost.CreateChannelConfig(channelId: "dingtalk-app-1", appKey: TestAppKey)];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        var channel = Assert.Single(probe.Channels, c => c.ChannelType == "dingtalk_oauth");
        Assert.Equal("dingtalk_oauth", channel.ChannelType);
        Assert.Equal("dingtalk-app-1", channel.ChannelId); // ChannelId = 配置应用 id（Options.Channels 选区）
    }

    [Fact]
    public async Task AuthenticateAsync_ValidCode_ReturnsOpenId_Success()
    {
        using var _ = this;
        var (_, channel) = Setup();
        var context = DingTalkTestHost.CreateContext(("code", "oauth-code-123"));

        var result = await channel.AuthenticateAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenId, result.ExternalUserId); // external_uid 恒 = openId（应用维度稳定标识）
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel); // 对齐微信便捷语义
    }

    [Fact]
    public async Task AuthenticateAsync_MissingCode_Fails_DINGTALK_CODE_REQUIRED()
    {
        using var _ = this;
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(("state", "abc")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DINGTALK_CODE_REQUIRED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_ApiError_Fails_WithMessage()
    {
        // 钉钉 API 返回 400（code 非法/已消费）→ 失败 + FailReason 携带错误消息
        using var _ = this;
        var (_, channel) = Setup(failOauth: true);

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(("code", "stale-code")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_ResponseMissingOpenId_Fails()
    {
        // 响应缺 openId 字段（scope 未含 openid）→ 失败（external_uid 恒 = openId 不可缺失）
        using var _ = this;
        var (_, channel) = Setup(openId: null);

        var result = await channel.AuthenticateAsync(
            DingTalkTestHost.CreateContext(("code", "scope-code")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DINGTALK_OPENID_MISSING", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }
}
