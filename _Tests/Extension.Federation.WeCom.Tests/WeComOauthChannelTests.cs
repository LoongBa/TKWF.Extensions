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
/// <para>v0.3.0 多通道：真实实例经 <see cref="ISsoChannelFactory"/> 按 channelId 构造（凭证从 registry Extra 懒加载）。</para>
/// </summary>
public class WeComOauthChannelTests : WeComChannelTestHost
{
    private const string TestCorpId = "ww_test_corp";
    private const string TestUserId = "usr_123";
    private const string TestOpenUserId = "ou_test_1";
    private const string TestOpenId = "oa_test_openid_1";
    private const string TestChannelId = "wecom-main";

    private async Task<(DomainUser<TestUserInfo> User, WeComOauthChannel Channel)> Setup(
        WeComMemberType memberType = WeComMemberType.SelfMember,
        bool isThirdParty = false,
        bool failOauth = false,
        bool enableSensitiveInfo = false,
        string agentId = "1000002")
    {
        Handler = WeComTestHost.CreateStubHandler(memberType, corpId: TestCorpId, failOauth: failOauth);
        ConfigureOptions = o => o.Channels =
        [
            WeComTestHost.CreateChannelConfig(channelId: TestChannelId, corpId: TestCorpId, isThirdParty: isThirdParty, enableSensitiveInfo: enableSensitiveInfo, agentId: agentId),
        ];
        var (_, user) = BindTestScope();
        // v0.3.0：经 ISsoChannelFactory 按 channelId 构造真实通道（凭证从 registry Extra 懒加载）
        var channel = await user.Use<ISsoChannelFactory>().CreateAsync(TestChannelId, "wecom_oauth");
        return (user, Assert.IsType<WeComOauthChannel>(channel));
    }

    [Fact]
    public async Task ChannelType_IsWecomOauth_AndChannelId_FromFactory()
    {
        ConfigureOptions = o => o.Channels = [WeComTestHost.CreateChannelConfig(channelId: TestChannelId)];
        var (_, user) = BindTestScope();
        // 探针枚举集合（模板实例——ChannelType 标识注册）……
        var probe = user.Use<IChannelProbe>();
        var template = Assert.Single(probe.Channels, c => c.ChannelType == "wecom_oauth");
        Assert.Equal("wecom_oauth", template.ChannelType);
        // ……真实实例经工厂构造（ChannelId = 注册表选区键）
        var channel = await user.Use<ISsoChannelFactory>().CreateAsync(TestChannelId, "wecom_oauth");
        Assert.NotNull(channel);
        Assert.Equal(TestChannelId, channel!.ChannelId);
    }

    [Fact]
    public async Task AuthenticateAsync_SelfMember_ReturnsCompositeCorpIdUserId()
    {
        // 自建应用企业成员——external_uid = {CorpId}:{userid} 复合（P0-1 双策略：互联企业 CorpId 消歧）
        using var _ = this;
        var (_, channel) = await Setup(WeComMemberType.SelfMember, isThirdParty: false);

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
        var (_, channel) = await Setup(WeComMemberType.ThirdPartyMember, isThirdParty: true);

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
        var (_, channel) = await Setup(WeComMemberType.NonMember, isThirdParty: false);

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
        var (_, channel) = await Setup();

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
        var (_, channel) = await Setup(failOauth: true);

        var result = await channel.AuthenticateAsync(
            WeComTestHost.CreateContext(("code", "stale-code")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailReason);
        Assert.Null(result.ExternalUserId);
    }

    [Fact]
    public async Task Ctor_EnableSensitiveInfoWithoutAgentId_FailsFast()
    {
        // Oracle 评审 P1-5：snsapi_privateinfo 授权必填 AgentId——启用且缺 → 构造 fail-fast（不静默降级）。
        // v0.3.0：模板实例（channel=null）无配置可校验；经工厂构造真实实例（channel 非空）时 ctor 抛
        ConfigureOptions = o => o.Channels =
        [
            WeComTestHost.CreateChannelConfig(channelId: TestChannelId, corpId: TestCorpId, enableSensitiveInfo: true, agentId: ""),
        ];
        var (_, user) = BindTestScope();
        await Assert.ThrowsAnyAsync<Exception>(
            () => user.Use<ISsoChannelFactory>().CreateAsync(TestChannelId, "wecom_oauth"));
    }
}
