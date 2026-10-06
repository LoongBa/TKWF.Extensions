using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using Xunit;

namespace TKWF.Federation.WeCom.Tests;

/// <summary>
/// <see cref="WeComOauthChannel"/>（wecom_oauth——企业微信双授权流统一身份获取）测试——生产路径宿主
/// （真实 DI + <c>AddWeComFederationChannels</c> + 探针门面帧内枚举）+ Stub 企业微信 API。
/// <para>验收（M4 方案 F2）：<b>external_uid 三路径双策略（Oracle 评审 P0-1）</b>——三方成员 open_userid /
/// 自建成员 {CorpId}:{userid} 复合 / 非成员 openid；缺 code 拒；API 错误拒；AgentId fail-fast（P1-5）。</para>
/// </summary>
public class WeComOauthChannelTests : WeComChannelTestHost
{
    private const string TestCorpId = "ww_test_corp";
    private const string TestUserId = "usr_123";
    private const string TestOpenUserId = "ou_test_1";
    private const string TestOpenId = "oa_test_openid_1";

    private (DomainUser<TestUserInfo> User, WeComOauthChannel Channel) Setup(
        WeComMemberType memberType = WeComMemberType.SelfMember,
        bool isThirdParty = false,
        bool failOauth = false,
        bool enableSensitiveInfo = false,
        string agentId = "1000002")
    {
        Handler = WeComTestHost.CreateStubHandler(memberType, corpId: TestCorpId, failOauth: failOauth);
        ConfigureOptions = o => o.Channels =
        [
            WeComTestHost.CreateChannelConfig(corpId: TestCorpId, isThirdParty: isThirdParty, enableSensitiveInfo: enableSensitiveInfo, agentId: agentId),
        ];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        var channel = Assert.IsType<WeComOauthChannel>(
            Assert.Single(probe.Channels, c => c.ChannelType == "wecom_oauth"));
        return (user, channel);
    }

    [Fact]
    public void ChannelType_IsWecomOauth_AndChannelIdFromOptions()
    {
        ConfigureOptions = o => o.Channels = [WeComTestHost.CreateChannelConfig(channelId: "wecom-app-1")];
        var (_, user) = BindTestScope();
        var probe = user.Use<IChannelProbe>();
        var channel = Assert.Single(probe.Channels, c => c.ChannelType == "wecom_oauth");
        Assert.Equal("wecom_oauth", channel.ChannelType);
        Assert.Equal("wecom-app-1", channel.ChannelId); // ChannelId = 配置应用 id（Options.Channels 选区）
    }

    [Fact]
    public async Task AuthenticateAsync_SelfMember_ReturnsCompositeCorpIdUserId()
    {
        // 自建应用企业成员——external_uid = {CorpId}:{userid} 复合（P0-1 双策略：互联企业 CorpId 消歧）
        using var _ = this;
        var (_, channel) = Setup(WeComMemberType.SelfMember, isThirdParty: false);

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(("code", "oauth-code-1")), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal($"{TestCorpId}:{TestUserId}", result.ExternalUserId);
        Assert.Null(result.FailReason);
        Assert.Equal(2, result.AuthLevel);
    }

    [Fact]
    public async Task AuthenticateAsync_ThirdPartyMember_ReturnsOpenUserId()
    {
        // 三方应用企业成员——external_uid = open_userid（全局唯一零复用风险，直接作映射主键）
        using var _ = this;
        var (_, channel) = Setup(WeComMemberType.ThirdPartyMember, isThirdParty: true);

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(("code", "oauth-code-3rd")), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenUserId, result.ExternalUserId); // open_userid 优先（非 {CorpId}:{userid}）
        Assert.Null(result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_NonMember_ReturnsOpenId()
    {
        // 非企业成员——external_uid = openid（对当前企业唯一）
        using var _ = this;
        var (_, channel) = Setup(WeComMemberType.NonMember, isThirdParty: false);

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(("code", "oauth-code-openid")), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(TestOpenId, result.ExternalUserId);
        Assert.Null(result.FailReason);
    }

    [Fact]
    public async Task AuthenticateAsync_MissingCode_Fails_WECOM_CODE_REQUIRED()
    {
        using var _ = this;
        var (_, channel) = Setup();

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(("state", "abc")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("WECOM_CODE_REQUIRED", result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task AuthenticateAsync_ApiError_Fails_WithMessage()
    {
        // 企业微信 API 返回 errcode（code 非法/已消费）→ 失败 + FailReason 携带错误消息
        using var _ = this;
        var (_, channel) = Setup(failOauth: true);

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(("code", "stale-code")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public void Ctor_EnableSensitiveInfoWithoutAgentId_FailsFast()
    {
        // Oracle 评审 P1-5：snsapi_privateinfo 授权必填 AgentId——启用且缺 → 启动 fail-fast（不静默降级）
        ConfigureOptions = o => o.Channels =
        [
            WeComTestHost.CreateChannelConfig(corpId: TestCorpId, enableSensitiveInfo: true, agentId: ""),
        ];
        Assert.ThrowsAny<Exception>(() =>
        {
            var (_, user) = BindTestScope();
            user.Use<IChannelProbe>();  // 帧内枚举 ISsoChannel 集合 → WeComOauthChannel ctor 抛 fail-fast
        });
    }
}
