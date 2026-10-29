using System;
using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Federation;

/// <summary>
/// provider × channelType 取值矩阵（D03 §6.1 评审 P2/缺陷 2 补充——契约合法组合防非法，
/// 如 <c>provider=wechat + channelType=alipay_mini_program</c> 须被拒绝）。
/// <para><b>开放注册表语义</b>：<see cref="Provider"/> 开放（wechat/alipay/douyin/...——新 provider 零 DDL，
/// 与 <see cref="ChannelConfig.PlatformType"/> 同构）；<see cref="ChannelType"/> 合法值由各 provider 定义，
/// 扩展为注册表字典条目（新 provider 新增条目）。</para>
/// <para><b>校验位置决策</b>（1.1 契约 §3.2）：<b>签票入口</b>（组装 payload 前）调用
/// <see cref="AssertLegal"/>——fail-fast 早暴露配置错误；兑付侧（Redeem 还原后）<b>不校验</b>
/// （载荷来自已密封票据，中间人保证来源可信——校验语义留给 C 侧业务）。</para>
/// </summary>
public static class ChannelTypeRegistry
{
    /// <summary>
    /// provider → 合法通道形态集（通道形态 = D03 ChannelType 语义：mp_subscription/mp_service/mini_program...
    /// <b>非</b>协议通道类型 wechat_oauth/wechat_event——协议类型经 payload Extensions["protocol_type"] 承载）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> LegalChannelTypes =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["wechat"] = ["mp_subscription", "mp_service", "mini_program", "work_wechat"],
            // 未来 provider 追加：["alipay"] = [...], ["douyin"] = [...]
        };

    /// <summary>provider 注册表（开放——新增 provider 在此登记，用于签票校验的 provider 存在性判定）。</summary>
    public static IReadOnlyCollection<string> Providers => [.. LegalChannelTypes.Keys];

    /// <summary>组合合法性判定（provider + channelType 均在矩阵内且配对合法）。</summary>
    public static bool IsLegal(string provider, string channelType)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(channelType))
            return false;
        return LegalChannelTypes.TryGetValue(provider, out var types)
            && types.Contains(channelType, StringComparer.Ordinal);
    }

    /// <summary>
    /// 签票入口断言（fail-fast——非法组合抛 <see cref="ArgumentException"/>，消息含合法值提示）。
    /// <para>⚠️ 命名碰撞红线（Oracle6 条件 4）：<paramref name="channelType"/> 是<b>通道形态</b>
    /// （mp_service/mp_subscription...）——误传协议类型（wechat_oauth/wechat_event）会被本断言拦截。</para>
    /// </summary>
    public static void AssertLegal(string provider, string channelType)
    {
        if (IsLegal(provider, channelType))
            return;
        var legal = LegalChannelTypes.TryGetValue(provider, out var types)
            ? string.Join(", ", types)
            : "(provider 未注册——请先登记 ChannelTypeRegistry)";
        throw new ArgumentException(
            $"非法 provider×channelType 组合：provider={provider}, channelType={channelType}；" +
            $"该 provider 合法通道形态：{legal}（channelType 为通道形态，非协议类型 wechat_oauth/wechat_event）",
            nameof(channelType));
    }
}