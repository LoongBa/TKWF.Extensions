using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
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

        var source = CreateSource(provider);
        source.BindDomainUser(user);
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

        var source = CreateSource(provider);
        source.BindDomainUser(user);
        var row = Assert.Single(source.GetChannels(), c => c.ChannelId == "d03-disabled");
        Assert.False(row!.IsEnabled);   // disabled 行保留（非过滤）
    }

    /// <summary>
    /// 凭证收敛点（审核 P0-2 修复后语义）：<c>AppSecret</c> 不进公共列（null，出站经 resolver），
    /// <b><c>Token</c>/<c>EncodingAESKey</c> 解密为明文进 Extra</b>——与静态源同语义，
    /// <c>WeChatEventChannel</c> 验签/解密可直接消费（密文承载会使 DB 通道事件验签恒失败）。
    /// </summary>
    [Fact]
    public async Task GetChannels_ProjectsPlaintextCredentials_SignatureVerifies()
    {
        using var _ = this;
        var (provider, user) = BindScope();

        var svc = user.Use<IChannelConfigService>();
        var aesKey = WeChatOperationsTestSupport.GenerateEncodingAESKey();
        await svc.SaveAsync(new SaveChannelRequest(
            Id: null, ChannelId: "d03-sec", AppId: "wx-d03-s", DisplayName: "安全号",
            ChannelType: "wechat_event", AppSecret: "secret-plain", Token: "token-plain", EncodingAesKey: aesKey),
            ct: TestContext.Current.CancellationToken);

        var source = CreateSource(provider);
        source.BindDomainUser(user);

        var row = Assert.Single(source.GetChannels(), c => c.ChannelId == "d03-sec");
        Assert.Null(row!.AppSecret);                                  // 出站凭证不进公共列
        Assert.Equal("token-plain", row.Extra["Token"]);              // 明文（非密文）
        Assert.Equal(aesKey, row.Extra["EncodingAESKey"]);

        // 收敛证明：投影 Token 经微信 4 参签名算法得硬编码期望值（外部事实源，非自反比较）
        const string timestamp = "1700000000";
        const string nonce = "d03-nonce";
        const string msgEncrypt = "cipher-payload-d03";
        Assert.Equal(
            "ca1c20789494c3cc72a2f6448dae8bc549d6296c",
            WeChatSignature(row.Extra["Token"]!, timestamp, nonce, msgEncrypt));
    }

    private static string WeChatSignature(string token, string timestamp, string nonce, string msgEncrypt)
    {
        var parts = new[] { token, timestamp, nonce, msgEncrypt };
        Array.Sort(parts, StringComparer.Ordinal);
        var hash = System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Concat(parts)));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// 审核 P0-1 回归：未绑定 DomainUser（脱离 Composite 装配链）→ fail-closed 空列表且不抛出。
    /// 修复前该场景经 <c>IServiceProvider</c> 直取 DataService 在生产必抛（被 catch-all 吞成恒空）。
    /// </summary>
    [Fact]
    public async Task GetChannels_WithoutBoundUser_FailClosedEmpty()
    {
        using var _ = this;
        var (provider, user) = BindScope();

        await user.Use<IChannelConfigService>().SaveAsync(new SaveChannelRequest(
            Id: null, ChannelId: "d03-unbound", AppId: "wx-d03-u", DisplayName: "有行但未绑定",
            ChannelType: "wechat_event"),
            ct: TestContext.Current.CancellationToken);

        var source = CreateSource(provider);
        Assert.Empty(source.GetChannels());
    }

    /// <summary>
    /// 审核 P0-1 回归（生产路径）：经 <c>User.Use&lt;IChannelRegistry&gt;()</c> → Composite 构造时绑定用户 →
    /// DB 源以 <c>IDomainUser.Use&lt;DataService&gt;()</c> NoAop 直建取数（不经 DI 注册，生产同形态）。
    /// </summary>
    [Fact]
    public async Task GetAllAsync_ViaComposite_ProjectsDbRows()
    {
        using var _ = this;
        var (_, user) = BindScope();

        await user.Use<IChannelConfigService>().SaveAsync(new SaveChannelRequest(
            Id: null, ChannelId: "d03-composite", AppId: "wx-d03-c", DisplayName: "组合路径",
            ChannelType: "wechat_event", ChannelForm: "mp_service", Verification: "verified", Token: "tok-composite"),
            ct: TestContext.Current.CancellationToken);

        var all = await user.Use<TKWF.Ext.Federation.IChannelRegistry>()
            .GetAllAsync(TestContext.Current.CancellationToken);

        var row = Assert.Single(all, c => c.ChannelId == "d03-composite");
        Assert.Equal("tok-composite", row!.Extra["Token"]);
        Assert.Equal("mp_service", row.Extra["channel_form"]);
    }

    private static WeChatChannelRegistrySource CreateSource(IServiceProvider provider)
        => new(
            provider.GetRequiredKeyedService<TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider>(
                WeChatOperationsKeyProviderKeys.WeChatOperations),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatChannelRegistrySource>.Instance);
}