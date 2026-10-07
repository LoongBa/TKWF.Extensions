using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;
using TKWF.Federation.Oidc;

namespace TKWF.Federation.Microsoft;

/// <summary>
/// Microsoft 静态通道来源（多通道联邦 v0.3.0 Phase 1——把 <see cref="MicrosoftOptions.Channels"/>
/// （<see cref="MicrosoftChannelConfig"/> 列表）投影为统一 <see cref="ChannelConfig"/>，供
/// <see cref="StaticChannelRegistry"/> 聚合选区）。
/// <para>投影映射（方案 §3.1 公共列 + Extra）：<c>ChannelId→ChannelId</c> / <c>ClientId→AppId</c> /
/// <c>ClientSecret→AppSecret</c>（公共列——OIDC 系 client_id/client_secret 按 M7 映射约束归一）；
/// <c>Tenant/TokenIssuers→Extra</c>（Tenant 为端点模板维度；TokenIssuers JSON 数组——iss 白名单正则防分隔符歧义）。
/// <see cref="PlatformType"/> = "microsoft"。</para>
/// <para>注册：<c>AddMicrosoftFederationChannels()</c> 内 <c>TryAddEnumerable&lt;IChannelSource&gt;</c>
/// （普通 DI 集合——非 IDomainService 无守卫工厂语义）。凭证自持（Oracle P2-4）不失效，仅投影形态统一。</para>
/// </summary>
public sealed class MicrosoftChannelSource : IChannelSource
{
    private readonly IOptions<MicrosoftOptions> _options;

    /// <summary>构造——Options 注入（读 TKWF:Federation:Microsoft 节）。</summary>
    public MicrosoftChannelSource(IOptions<MicrosoftOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string PlatformType => "microsoft";

    /// <inheritdoc />
    public IReadOnlyList<ChannelConfig> GetChannels()
        => _options.Value.Channels
            .Select(c => new ChannelConfig
            {
                ChannelId = c.ChannelId,
                PlatformType = PlatformType,
                AppId = string.IsNullOrWhiteSpace(c.ClientId) ? null : c.ClientId,
                AppSecret = string.IsNullOrWhiteSpace(c.ClientSecret) ? null : c.ClientSecret,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [OidcChannelConfigKeys.Platform] = "microsoft",
                    [OidcChannelConfigKeys.Tenant] = c.Tenant,
                    [OidcChannelConfigKeys.TokenIssuers] = JsonSerializer.Serialize(c.TokenIssuers),
                },
            })
            .ToList();
}
