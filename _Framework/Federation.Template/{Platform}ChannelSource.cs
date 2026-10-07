using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;

namespace TKWF.Federation.{Namespace};

/// <summary>
/// {平台}静态通道来源（多通道联邦 v0.3.0 Phase 1——把 <see cref="{Platform}Options.Channels"/> 投影为统一
/// <see cref="ChannelConfig"/>，供 <see cref="StaticChannelRegistry"/> 聚合选区）。
/// <para>投影映射（方案 §3.1 公共列 + Extra）：<c>ChannelId→ChannelId</c> / <c>AppId→AppId</c> /
/// <c>AppSecret→AppSecret</c>（公共列）；平台特有字段（Token/EncodingAESKey/CorpId/AgentId 等入站信任根、
/// 法人级标识——映射规则约束 M7）进 <see cref="ChannelConfig.Extra"/>。多环境（沙箱/生产）经
/// Extra["Environment"] 承载（方案 M11 前瞻）。</para>
/// <para>注册：<c>Add{Platform}FederationChannels()</c> 内 <c>TryAddEnumerable&lt;IChannelSource&gt;</c>
/// （普通 DI 集合——非 IDomainService 无守卫工厂语义）。凭证自持（Oracle P2-4）不失效，仅投影形态统一。</para>
/// </summary>
public sealed class {Platform}ChannelSource : IChannelSource
{
    private readonly IOptions<{Platform}Options> _options;

    /// <summary>构造——Options 注入（读 TKWF:Federation:{Namespace} 节）。</summary>
    public {Platform}ChannelSource(IOptions<{Platform}Options> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string PlatformType => "{platform}";

    /// <inheritdoc />
    public IReadOnlyList<ChannelConfig> GetChannels()
        => _options.Value.Channels
            .Select(c => new ChannelConfig
            {
                ChannelId = c.ChannelId,
                PlatformType = PlatformType,
                AppId = c.AppId,
                AppSecret = c.AppSecret,
                Extra = BuildExtra(c),
            })
            .ToList();

    /// <summary>平台特有负载 → Extra（有入站回调平台追加 Token/EncodingAESKey——钥匙信任根；平台差异点在此投射）。
    /// ⚠️ 平台差异：字段名与公共列映射（AppId/AppSecret 承载与否）以 {Platform}ApiClient 凭证解析机制为准。</summary>
    private static IReadOnlyDictionary<string, string?> BuildExtra({Platform}ChannelConfig c) => new Dictionary<string, string?>(StringComparer.Ordinal);
}