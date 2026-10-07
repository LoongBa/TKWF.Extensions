using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;

namespace TKWF.Federation.DingTalk;

/// <summary>
/// 钉钉静态通道来源（多通道联邦 v0.3.0 Phase 1——把 <see cref="DingTalkOptions.Channels"/> 投影为统一
/// <see cref="ChannelConfig"/>，供 <see cref="StaticChannelRegistry"/> 聚合选区）。
/// <para>投影映射（方案 §3.1 公共列 + Extra）：<c>ChannelId→ChannelId</c> / <c>AppSecret→AppSecret</c>
/// （公共列）/ <c>CorpId/AppKey/Token/EncodingAESKey→Extra</c>（平台特定——CorpId 法人级标识 + 事件验签
/// 三道闸门信任根负载 + AppKey 钉钉凭证解析键，进 Extra 不挤占 AppId——映射规则约束 M7）。
/// <see cref="PlatformType"/> = "dingtalk"。</para>
/// <para>注册：<c>AddDingTalkFederationChannels()</c> 内 <c>TryAddEnumerable&lt;IChannelSource&gt;</c>
/// （普通 DI 集合——非 IDomainService 无守卫工厂语义）。凭证自持（Oracle P2-4）不失效，仅投影形态统一。</para>
/// </summary>
public sealed class DingTalkChannelSource : IChannelSource
{
    private readonly IOptions<DingTalkOptions> _options;

    /// <summary>构造——Options 注入（读 TKWF:Federation:DingTalk 节）。</summary>
    public DingTalkChannelSource(IOptions<DingTalkOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string PlatformType => "dingtalk";

    /// <inheritdoc />
    public IReadOnlyList<ChannelConfig> GetChannels()
        => _options.Value.Channels
            .Select(c => new ChannelConfig
            {
                ChannelId = c.ChannelId,
                PlatformType = PlatformType,
                AppSecret = c.AppSecret,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["CorpId"] = c.CorpId,
                    ["AppKey"] = c.AppKey,
                    ["Token"] = c.Token,
                    ["EncodingAESKey"] = c.EncodingAESKey,
                },
            })
            .ToList();
}
