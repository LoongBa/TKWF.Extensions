using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;

namespace TKWF.Federation.QQ;

/// <summary>
/// QQ 静态通道来源（多通道联邦 v0.3.0 Phase 1——把 <see cref="QqOptions.Channels"/> 投影为统一
/// <see cref="ChannelConfig"/>，供 <see cref="StaticChannelRegistry"/> 聚合选区）。
/// <para>投影映射（方案 §3.1 公共列 + Extra）：<c>ChannelId→ChannelId</c> / <c>AppId→AppId</c> /
/// <c>AppSecret→AppSecret</c>（公共列）；<c>EnableUnionId→Extra</c>（QQ 特有开关——N3 P1-2 unionid 可选增强）。
/// QqApiClient 凭证解析机制保留（Options 按 AppId 精确匹配），通道只传 AppId。</para>
/// <para>注册：<c>AddQqFederationChannels()</c> 内 <c>TryAddEnumerable&lt;IChannelSource&gt;</c>
/// （普通 DI 集合——非 IDomainService 无守卫工厂语义）。凭证自持（Oracle P2-4）不失效，仅投影形态统一。</para>
/// </summary>
public sealed class QqChannelSource : IChannelSource
{
    private readonly IOptions<QqOptions> _options;

    /// <summary>构造——Options 注入（读 TKWF:Federation:QQ 节）。</summary>
    public QqChannelSource(IOptions<QqOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string PlatformType => "qq";

    /// <inheritdoc />
    public IReadOnlyList<ChannelConfig> GetChannels()
        => _options.Value.Channels
            .Select(c => new ChannelConfig
            {
                ChannelId = c.ChannelId,
                PlatformType = PlatformType,
                Alias = c.Alias,   // 对外别名（可空——缺省 null = 用 ChannelId 对外，方案 §3.7 双键）
                AppId = c.AppId,
                AppSecret = c.AppSecret,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["EnableUnionId"] = c.EnableUnionId.ToString(),
                },
            })
            .ToList();
}