using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信静态通道来源（多通道联邦 v0.3.0 Phase 1——把 <see cref="WeChatOptions.Channels"/> 投影为统一
/// <see cref="ChannelConfig"/>，供 <see cref="StaticChannelRegistry"/> 聚合选区）。
/// <para>投影映射（方案 §3.1 公共列 + Extra）：<c>ChannelId→ChannelId</c> / <c>AppId→AppId</c> /
/// <c>AppSecret→AppSecret</c>（公共列）/ <c>Token/EncodingAESKey→Extra</c>（平台特定——事件验签信任根负载，
/// 法人级/入站密钥类字段进 Extra，不挤占 AppId——映射规则约束 M7）。<see cref="PlatformType"/> = "wechat"。</para>
/// <para>注册：<c>AddWeChatFederationChannels()</c> 内 <c>TryAddEnumerable&lt;IChannelSource&gt;</c>
/// （普通 DI 集合——非 IDomainService 无守卫工厂语义）。凭证自持（Oracle P2-4）不失效，仅投影形态统一。</para>
/// </summary>
public sealed class WeChatChannelSource : IChannelSource
{
    private readonly IOptions<WeChatOptions> _options;

    /// <summary>构造——Options 注入（读 TKWF:Federation:WeChat 节）。</summary>
    public WeChatChannelSource(IOptions<WeChatOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string PlatformType => "wechat";

    /// <summary>能力声明（I2 §3.4——微信首批：事件 + 关注事件 + 状态反查 + 菜单出站；SilentAuthorize 视认证服务号）。
    /// <para>判据（§3.4）：能力不足 → 编排层直接跳过（不报错不降级）；本平台声明 Event|Follow|FollowQuery|
    /// Menu 恒成立（事件推送解析 + 关注事件产生 + cgi-bin/user/info 反查 + 菜单 API 已实现——WeChatEventChannel/
    /// WeChatApiClient）；SilentAuthorize 按配置（未认证服务号 → 不声明，免登入口自然降级——§6 微信免登闭环）。</para></summary>
    public ChannelCapabilities Capabilities
    {
        get
        {
            var caps = ChannelCapabilities.Event | ChannelCapabilities.Follow
                | ChannelCapabilities.FollowQuery | ChannelCapabilities.Menu;
            var any = _options.Value.Channels;
            if (any.Any(c => !string.IsNullOrEmpty(c.AppId) && !string.IsNullOrEmpty(c.AppSecret)))
                caps |= ChannelCapabilities.SilentAuthorize;
            return caps;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ChannelConfig> GetChannels()
        => _options.Value.Channels
            .Select(c => Build(c))
            .ToList();

    private static ChannelConfig Build(WeChatChannelConfig c)
    {
        var extra = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Token"] = c.Token,
            ["EncodingAESKey"] = c.EncodingAESKey,
        };
        if (c.NewsReplyTemplate is not null)
            extra[WechatEventReplyHandlerBase.NewsReplyTemplateKey] = JsonSerializer.Serialize(c.NewsReplyTemplate);
        return new ChannelConfig
        {
            ChannelId = c.ChannelId,
            PlatformType = "wechat",
            Alias = c.Alias,
            AppId = c.AppId,
            AppSecret = c.AppSecret,
            Extra = extra,
        };
    }
}