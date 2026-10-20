using System;

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道能力声明（I2 方案 §3.4 Q5——显式声明而非 try/catch 探测：探测把「平台不支持」和「平台故障」混为一谈，
/// 且每次调用付出一次失败开销）。<b>DIM 默认 <see cref="None"/> = 刻意 fail-closed</b>：新增平台库忘记声明 →
/// 关注域静默不记录（而非「试着调一下看报不报错」）。
/// <para>消费点：<c>EventOrchestrator</c> 记录前检查 <see cref="Follow"/>；<c>SubscriptionStat</c> 派生检查
/// <see cref="Follow"/>；免登入口检查 <see cref="SilentAuthorize"/>。</para>
/// </summary>
[Flags]
public enum ChannelCapabilities
{
    None            = 0,
    OAuth           = 1 << 0,   // 支持 AuthenticateAsync / BuildAuthorizeUrlAsync
    Event           = 1 << 1,   // 支持 HandleEventAsync
    Follow          = 1 << 2,   // 会产生关注/取关事件（供关注域记录）
    FollowQuery     = 1 << 3,   // 支持向平台反查当前关注状态（IFollowChannelQuery）
    SilentAuthorize = 1 << 4,   // 支持 snsapi_base 静默授权（免登入口前置）
    Menu            = 1 << 5    // 支持菜单管理出站
}