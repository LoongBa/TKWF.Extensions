namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 订阅统计来源/场景常量（V0.3.0——来源/场景维度，DMP-Lite 5.7"来源/场景筛选：搜索/扫码/名片/分享卡片/小程序/三种取关场景"；
/// 对齐 <see cref="WeChatReplySceneTypes"/> 风格——public static class const 字符串）。
/// <para>值语义（I2-10 派生改造后）：关注域 <see cref="TKWF.Ext.Federation.IFollowStateService"/> 记录事件时
/// 经 <c>ChannelEvent.Source</c> 落 <c>FollowEventEntity.Source</c> 列；<see cref="ISubscriptionStatService"/>
/// 趋势/汇总查询的 <c>source</c> 参数按此值过滤（空 = 全来源合计；非空 = 仅该来源维度）。</para>
/// </summary>
public static class WeChatSubscriptionSources
{
    /// <summary>搜索公众号关注（微信"搜一搜"命中公众号关注）。</summary>
    public const string Search = "search";

    /// <summary>扫码关注（已关注用户扫码——SCAN 场景）。</summary>
    public const string Scan = "scan";

    /// <summary>二维码关注（带参二维码——<c>qrscene_</c> 前缀场景值）。</summary>
    public const string QrCode = "qr_code";

    /// <summary>名片分享关注（他人名片转发关注）。</summary>
    public const string Card = "card";

    /// <summary>分享卡片关注（图文/消息分享后关注）。</summary>
    public const string Share = "share";

    /// <summary>小程序关注（公众号关联小程序跳转关注）。</summary>
    public const string Miniprogram = "miniprogram";
}
