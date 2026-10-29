using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.Federation;
using TKWF.Ext.WeChatOperations.DTOs;
using Xunit;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// D03 1.7 微信通道能力派生矩阵测试（方案 §4.3 —— (ChannelForm, Verification) → ChannelCapabilities 全矩阵）。
/// <para>矩阵定稿（微信平台事实）：订阅号认证后仍无网页授权；小程序授权域独立；企微对接面预留；未知形态 fail-closed。</para>
/// </summary>
public class WeChatChannelCapabilityResolverTests
{
    // mp_subscription × none/verified → Event | Follow（认证后仍无网页授权——订阅号事实）
    [Theory]
    [InlineData(WeChatChannelCapabilityResolver.Verifications.None)]
    [InlineData(WeChatChannelCapabilityResolver.Verifications.Verified)]
    public void MpSubscription_EventFollow_RegardlessOfVerification(string verification)
    {
        var caps = WeChatChannelCapabilityResolver.Resolve(
            WeChatChannelCapabilityResolver.Forms.MpSubscription, verification);
        Assert.Equal(ChannelCapabilities.Event | ChannelCapabilities.Follow, caps);
    }

    // mp_service × none → Event | Follow（未认证无网页授权）
    [Fact]
    public void MpService_None_EventFollow()
    {
        var caps = WeChatChannelCapabilityResolver.Resolve(
            WeChatChannelCapabilityResolver.Forms.MpService, WeChatChannelCapabilityResolver.Verifications.None);
        Assert.Equal(ChannelCapabilities.Event | ChannelCapabilities.Follow, caps);
    }

    // mp_service × verified → 全套（OAuth/SilentAuthorize/FollowQuery/Menu 全开）
    [Fact]
    public void MpService_Verified_FullCapabilities()
    {
        var caps = WeChatChannelCapabilityResolver.Resolve(
            WeChatChannelCapabilityResolver.Forms.MpService, WeChatChannelCapabilityResolver.Verifications.Verified);
        Assert.Equal(
            ChannelCapabilities.Event | ChannelCapabilities.Follow | ChannelCapabilities.OAuth
                | ChannelCapabilities.SilentAuthorize | ChannelCapabilities.FollowQuery | ChannelCapabilities.Menu,
            caps);
    }

    // mini_program × 任意 → OAuth | SilentAuthorize | Event | Follow（小程序授权域独立）
    [Theory]
    [InlineData(WeChatChannelCapabilityResolver.Verifications.None)]
    [InlineData(WeChatChannelCapabilityResolver.Verifications.Verified)]
    public void MiniProgram_OAuth_Silent_Event_Follow(string verification)
    {
        var caps = WeChatChannelCapabilityResolver.Resolve(
            WeChatChannelCapabilityResolver.Forms.MiniProgram, verification);
        Assert.Equal(
            ChannelCapabilities.OAuth | ChannelCapabilities.SilentAuthorize
                | ChannelCapabilities.Event | ChannelCapabilities.Follow,
            caps);
    }

    // work_wechat × 任意 → OAuth | Menu（企微对接面；预留）
    [Theory]
    [InlineData(WeChatChannelCapabilityResolver.Verifications.None)]
    [InlineData(WeChatChannelCapabilityResolver.Verifications.Verified)]
    public void WorkWechat_OAuth_Menu(string verification)
    {
        var caps = WeChatChannelCapabilityResolver.Resolve(
            WeChatChannelCapabilityResolver.Forms.WorkWechat, verification);
        Assert.Equal(ChannelCapabilities.OAuth | ChannelCapabilities.Menu, caps);
    }

    // other / 未知形态 / 空值 → None（fail-closed）
    [Fact]
    public void UnknownForm_FailClosedNone()
    {
        Assert.Equal(ChannelCapabilities.None, WeChatChannelCapabilityResolver.Resolve("unknown", "verified"));
        Assert.Equal(ChannelCapabilities.None, WeChatChannelCapabilityResolver.Resolve(WeChatChannelCapabilityResolver.Forms.Other, "whatever"));
        Assert.Equal(ChannelCapabilities.None, WeChatChannelCapabilityResolver.Resolve(null, null));
        Assert.Equal(ChannelCapabilities.None, WeChatChannelCapabilityResolver.Resolve("", ""));
    }

    // 大小写/空白归一（ToLowerInvariant 归一——配置录入宽容）
    [Fact]
    public void CaseInsensitive_InputNormalized()
    {
        var caps = WeChatChannelCapabilityResolver.Resolve("MP_SERVICE", "VERIFIED");
        Assert.Equal(
            ChannelCapabilities.Event | ChannelCapabilities.Follow | ChannelCapabilities.OAuth
                | ChannelCapabilities.SilentAuthorize | ChannelCapabilities.FollowQuery | ChannelCapabilities.Menu,
            caps);
    }
}

/// <summary>
/// D03 1.7 <see cref="WeChatChannelRegistrySource"/> DB 源投影测试——Priority=100 +
/// ChannelForm/Verification/Capabilities/Alias 投影 + 全行投影（含 disabled，S3 判据）+ 凭证密文不进公共列。
/// </summary>
public class WeChatChannelRegistrySourceTests : WeChatOperationsTestHost
{
    /// <summary>Priority=100（DB 权威——恒先于静态源 0 / Federation 主包 SsoChannelRegistryEntity 系 0）。</summary>
    [Fact]
    public void Priority_Is100_DbAuthority()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        var source = provider.GetServices<TKWF.Ext.Federation.IChannelSource>().OfType<WeChatChannelRegistrySource>().Single();
        Assert.Equal(100, source.Priority);
    }

    /// <summary>投影全量——ChannelForm/Verification → Extra + Capabilities 派生 + Alias 双键（spect 全字段断言）。</summary>
    [Fact]
    public async Task GetChannels_ProjectsFormVerificationAliasCapabilities()
    {
        using var _ = this;
        var (provider, user) = BindScope();

        // 经门面落库（SaveAsync 新列写路径）——服务号认证（full caps）+ 订阅号未认证
        var svc = user.Use<IChannelConfigService>();
        await svc.SaveAsync(new SaveChannelRequest(
            Id: null, ChannelId: "d03-mp-1", AppId: "wx-d03-1", DisplayName: "服务号已认证",
            ChannelType: "wechat_event", ChannelForm: "mp_service", Verification: "verified",
            Alias: "merchant-d03", AppSecret: "s1", Token: "t1", EncodingAesKey: "e1"),
            ct: TestContext.Current.CancellationToken);
        await svc.SaveAsync(new SaveChannelRequest(
            Id: null, ChannelId: "d03-mp-2", AppId: "wx-d03-2", DisplayName: "订阅号",
            ChannelType: "wechat_event", ChannelForm: "mp_subscription", Verification: "none",
            AppSecret: "s2"),
            ct: TestContext.Current.CancellationToken);

        var source = provider.GetServices<TKWF.Ext.Federation.IChannelSource>().OfType<WeChatChannelRegistrySource>().Single();
        var channels = source.GetChannels();
        var mp1 = Assert.Single(channels, c => c.ChannelId == "d03-mp-1");
        Assert.Equal("merchant-d03", mp1!.Alias);
        Assert.Equal("mp_service", mp1.Extra["channel_form"]);
        Assert.Equal("verified", mp1.Extra["verification"]);
        Assert.Equal(
            ChannelCapabilities.Event | ChannelCapabilities.Follow | ChannelCapabilities.OAuth
                | ChannelCapabilities.SilentAuthorize | ChannelCapabilities.FollowQuery | ChannelCapabilities.Menu,
            mp1.Capabilities);

        var mp2 = Assert.Single(channels, c => c.ChannelId == "d03-mp-2");
        Assert.Equal(ChannelCapabilities.Event | ChannelCapabilities.Follow, mp2!.Capabilities);
    }

    /// <summary>全行投影（含 disabled）——IsEnabled=false 行保留（S3 降级判据：命中 disabled → 提示响应）。</summary>
    [Fact]
    public async Task GetChannels_IncludesDisabledRows_S3Judgement()
    {
        using var _ = this;
        var (provider, user) = BindScope();

        var svc = user.Use<IChannelConfigService>();
        await svc.SaveAsync(new SaveChannelRequest(
            Id: null, ChannelId: "d03-disabled", AppId: "wx-d03-d", DisplayName: "停用号",
            ChannelType: "wechat_event", IsEnabled: false),
            ct: TestContext.Current.CancellationToken);

        var source = provider.GetServices<TKWF.Ext.Federation.IChannelSource>().OfType<WeChatChannelRegistrySource>().Single();
        var row = Assert.Single(source.GetChannels(), c => c.ChannelId == "d03-disabled");
        Assert.False(row!.IsEnabled);   // disabled 行保留（非过滤）
    }

    /// <summary>凭证安全姿态（Oracle P1 安全 3 保留）：AppSecret 不进公共列（null）；Token/EncodingAESKey 密文进 Extra（不解密）。</summary>
    [Fact]
    public async Task GetChannels_CredentialSafePosture()
    {
        using var _ = this;
        var (provider, user) = BindScope();

        var svc = user.Use<IChannelConfigService>();
        var aesKey = WeChatOperationsTestSupport.GenerateEncodingAESKey();
        await svc.SaveAsync(new SaveChannelRequest(
            Id: null, ChannelId: "d03-sec", AppId: "wx-d03-s", DisplayName: "安全号",
            ChannelType: "wechat_event", AppSecret: "secret-plain", Token: "token-plain", EncodingAesKey: aesKey),
            ct: TestContext.Current.CancellationToken);

        var source = provider.GetServices<TKWF.Ext.Federation.IChannelSource>().OfType<WeChatChannelRegistrySource>().Single();
        var row = Assert.Single(source.GetChannels(), c => c.ChannelId == "d03-sec");
        Assert.Null(row!.AppSecret);                                  // 密文不进公共列
        Assert.DoesNotContain("secret-plain", row.Extra.Values);      // 明文不可见
        Assert.NotEqual("token-plain", row.Extra["Token"]);           // Token 密文承载
        Assert.NotEqual(aesKey, row.Extra["EncodingAESKey"]);         // EncodingAESKey 密文承载
    }
}