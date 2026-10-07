using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <inheritdoc />
    public IReadOnlyList<ChannelConfig> GetChannels()
        => _options.Value.Channels
            .Select(c => new ChannelConfig
            {
                ChannelId = c.ChannelId,
                PlatformType = PlatformType,
                AppId = c.AppId,
                AppSecret = c.AppSecret,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Token"] = c.Token,
                    ["EncodingAESKey"] = c.EncodingAESKey,
                },
            })
            .ToList();
}