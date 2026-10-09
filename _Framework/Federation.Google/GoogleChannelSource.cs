using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;
using TKWF.Federation.Oidc;

namespace TKWF.Federation.Google;

/// <summary>
/// Google 静态通道来源（多通道联邦 v0.3.0 Phase 1——把 <see cref="GoogleOptions.Channels"/>
/// （<see cref="GoogleChannelConfig"/> 列表）投影为统一 <see cref="ChannelConfig"/>，供
/// <see cref="StaticChannelRegistry"/> 聚合选区）。
/// <para>投影映射（方案 §3.1 公共列 + Extra）：<c>ChannelId→ChannelId</c> / <c>ClientId→AppId</c> /
/// <c>ClientSecret→AppSecret</c>（公共列——OIDC 系 client_id/client_secret 按 M7 映射约束归一）；
/// <c>TokenIssuers→Extra</c>（JSON 数组——iss 白名单正则防分隔符歧义）。<see cref="PlatformType"/> = "google"。</para>
/// <para>⚠️ Google 显式单实例语义（Oracle P1-3/S4）：投影形态统一但运行时只配单通道——
/// <c>google_oidc:*</c> public 通配天然不适用多 App（文档标注）。</para>
/// <para>注册：<c>AddGoogleFederationChannels()</c> 内 <c>TryAddEnumerable&lt;IChannelSource&gt;</c>
/// （普通 DI 集合——非 IDomainService 无守卫工厂语义）。凭证自持（Oracle P2-4）不失效，仅投影形态统一。</para>
/// </summary>
public sealed class GoogleChannelSource : IChannelSource
{
    private readonly IOptions<GoogleOptions> _options;

    /// <summary>构造——Options 注入（读 TKWF:Federation:Google 节）。</summary>
    public GoogleChannelSource(IOptions<GoogleOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string PlatformType => "google";

    /// <inheritdoc />
    public IReadOnlyList<ChannelConfig> GetChannels()
        => _options.Value.Channels
            .Select(c => new ChannelConfig
            {
                ChannelId = c.ChannelId,
                PlatformType = PlatformType,
                Alias = c.Alias,   // 对外别名（可空——缺省 null = 用 ChannelId 对外，方案 §3.7 双键）
                AppId = string.IsNullOrWhiteSpace(c.ClientId) ? null : c.ClientId,
                AppSecret = string.IsNullOrWhiteSpace(c.ClientSecret) ? null : c.ClientSecret,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [OidcChannelConfigKeys.Platform] = "google",
                    [OidcChannelConfigKeys.TokenIssuers] = JsonSerializer.Serialize(c.TokenIssuers),
                },
            })
            .ToList();
}
