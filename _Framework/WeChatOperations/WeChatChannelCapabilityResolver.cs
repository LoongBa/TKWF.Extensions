using System;
using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信通道能力派生（D03 §4.3——(ChannelForm, Verification) → <see cref="ChannelCapabilities"/>，派生不落库）。
/// <para>通道形态 × 认证状态矩阵（微信平台事实——订阅号认证后仍无网页授权；小程序授权域独立）：</para>
/// <code>
/// mp_subscription × none     → Event | Follow
/// mp_subscription × verified → Event | Follow
/// mp_service      × none     → Event | Follow
/// mp_service      × verified → Event | Follow | OAuth | SilentAuthorize | FollowQuery | Menu
/// mini_program    × (任意)    → OAuth | SilentAuthorize | Event | Follow
/// work_wechat     × (任意)    → OAuth | Menu（企微对接面；预留）
/// other           × (任意)    → None（fail-closed）
/// </code>
/// <para>消费点：<see cref="WeChatChannelRegistrySource"/> 投影时按实体行动态填充
/// <see cref="ChannelConfig.Capabilities"/>（per-channel 动态覆盖 <see cref="IChannelSource.Capabilities"/>
/// DIM 静态声明——Oracle6 条件 1 方案 A 定稿）；营业能力（模板消息/支付）不走本位（Extra 承载，见 D03）。</para>
/// </summary>
public static class WeChatChannelCapabilityResolver
{
    /// <summary>通道形态常量（<see cref="ChannelTypeRegistry"/> wechat 域合法值对齐）。</summary>
    public static class Forms
    {
        public const string MpSubscription = "mp_subscription";
        public const string MpService = "mp_service";
        public const string MiniProgram = "mini_program";
        public const string WorkWechat = "work_wechat";
        public const string Other = "other";
    }

    /// <summary>认证状态常量（D03 §4.1 Verification）。</summary>
    public static class Verifications
    {
        public const string None = "none";
        public const string Verified = "verified";
    }

    /// <summary>(ChannelForm, Verification) → 协议能力面（未知形态 fail-closed None）。</summary>
    public static ChannelCapabilities Resolve(string? channelForm, string? verification)
    {
        var form = string.IsNullOrWhiteSpace(channelForm) ? Forms.Other : channelForm.Trim().ToLowerInvariant();
        var verified = string.Equals(verification, Verifications.Verified, StringComparison.OrdinalIgnoreCase);

        return form switch
        {
            Forms.MpSubscription => ChannelCapabilities.Event | ChannelCapabilities.Follow,
            Forms.MpService => verified
                ? ChannelCapabilities.Event | ChannelCapabilities.Follow | ChannelCapabilities.OAuth
                    | ChannelCapabilities.SilentAuthorize | ChannelCapabilities.FollowQuery | ChannelCapabilities.Menu
                : ChannelCapabilities.Event | ChannelCapabilities.Follow,
            Forms.MiniProgram => ChannelCapabilities.OAuth | ChannelCapabilities.SilentAuthorize
                | ChannelCapabilities.Event | ChannelCapabilities.Follow,
            Forms.WorkWechat => ChannelCapabilities.OAuth | ChannelCapabilities.Menu,
            _ => ChannelCapabilities.None,   // other / 未知形态——fail-closed
        };
    }
}