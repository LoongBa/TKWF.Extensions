using System;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKWF.Ext.WeChatChannels;

namespace TKWF.Ext.WeChatChannels.Tests;

/// <summary>
/// <see cref="ChannelConfigService"/> 通道配置门面 B 档修复测试（2026-11-08）：
/// <para>① CH-2 ChannelType 白名单校验——未知协议通道类型拒透传存储（fail-fast——对齐 ChannelForm/Verification
/// 值校验形态；防下游平台库 ISsoChannel 选区永不命中）；</para>
/// <para>② CH-3 DTO 投影补 ChannelForm/Verification——管理面投影 DTO 透传实体两字段（外部消费方展示/判断用）；</para>
/// <para>③ CH-12 CAS 更新错误归因——更新不存在的通道 → DomainException ErrorCode=CHANNEL_NOT_FOUND
/// （目标不存在归因，区别于 CHANNEL_ALIAS_IMMUTABLE alias 冲突归因）。</para>
/// <para>生产路径：<c>user.Use&lt;IChannelConfigService&gt;()</c> AOP（AddConstructibleService 守卫工厂）。</para>
/// </summary>
public class ChannelConfigServiceBFixTests : WeChatChannelsTestHost
{
    /// <summary>CH-2 负测试：SaveChannelAsync 入参 ChannelType 不在白名单（{"wechat_oauth","wechat_event"}）
    /// → ArgumentException，消息含 "ChannelType" + 实际值（未知值拒透传存储）。</summary>
    [Fact]
    public async Task Save_UnknownChannelType_Throws()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IChannelConfigService>();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SaveChannelAsync(new SaveChannelRequest(
            Id: null,
            ChannelId: "ch-ct-1",
            AppId: "wx-ct-1",
            DisplayName: "非法协议类型",
            IsEnabled: true,
            ChannelType: "NotARealType"),
            ct: TestContext.Current.CancellationToken));

        Assert.Contains("ChannelType", ex.Message);
        Assert.Contains("NotARealType", ex.Message);
    }

    /// <summary>CH-2 正例：白名单合法值（含大小写变体——OrdinalIgnoreCase 白名单比对）归一化落库 "wechat_event"。</summary>
    [Fact]
    public async Task Save_ValidChannelType_NormalizedAndStored()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IChannelConfigService>();

        await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null,
            ChannelId: "ch-ct-2",
            AppId: "wx-ct-2",
            DisplayName: "合法协议类型",
            IsEnabled: true,
            ChannelType: " WECHAT_EVENT "),
            ct: TestContext.Current.CancellationToken);

        // 生产路径回读（服务层 DTO 投影——不经存储层直查，测试不引 FreeSql）
        var dto = await service.GetAsync("wx-ct-2", ct: TestContext.Current.CancellationToken);
        Assert.NotNull(dto);
        Assert.Equal("wechat_event", dto!.ChannelType);
    }

    /// <summary>CH-3：DTO 投影透传 ChannelForm/Verification——Create 时写入 mp_service/verified，
    /// GetAsync 查回 DTO 含两字段透传值（外部消费方展示/判断用）。</summary>
    [Fact]
    public async Task GetAsync_Dto_Projection_Includes_ChannelForm_And_Verification()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IChannelConfigService>();

        await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null,
            ChannelId: "ch-proj-1",
            AppId: "wx-proj-1",
            DisplayName: "投影透传",
            IsEnabled: true,
            ChannelType: "wechat_event",
            ChannelForm: "mp_service",
            Verification: "verified"),
            ct: TestContext.Current.CancellationToken);

        var dto = await service.GetAsync("wx-proj-1", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.Equal("mp_service", dto!.ChannelForm);
        Assert.Equal("verified", dto.Verification);
    }

    /// <summary>CH-3 默认值：实体缺省 mp_service/none——未显式传 ChannelForm 时 DTO 投影仍回读实体存量值。</summary>
    [Fact]
    public async Task GetAsync_Dto_Projection_Defaults_From_Entity()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IChannelConfigService>();

        await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null,
            ChannelId: "ch-proj-2",
            AppId: "wx-proj-2",
            DisplayName: "默认形态",
            IsEnabled: true,
            ChannelType: "wechat_event"),
            ct: TestContext.Current.CancellationToken);

        var dto = await service.GetAsync("wx-proj-2", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(dto);
        Assert.Equal("mp_service", dto!.ChannelForm);
        Assert.Equal("none", dto.Verification);
    }

    /// <summary>CH-12 语义：更新不存在的通道（Id 无对应行）→ DomainException ErrorCode=CHANNEL_NOT_FOUND
    /// （目标不存在归因——对齐 alias 守卫错误码形态；区别于 CHANNEL_ALIAS_IMMUTABLE alias 冲突归因）。</summary>
    [Fact]
    public async Task Save_UpdateNonExistentChannel_Throws_ChannelNotFound()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IChannelConfigService>();

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.SaveChannelAsync(new SaveChannelRequest(
            Id: 999_999,
            ChannelId: "ch-missing-1",
            AppId: "wx-missing-1",
            DisplayName: "不存在的通道",
            IsEnabled: true,
            ChannelType: "wechat_event"),
            ct: TestContext.Current.CancellationToken));

        Assert.Equal("CHANNEL_NOT_FOUND", ex.ErrorCode);
    }
}