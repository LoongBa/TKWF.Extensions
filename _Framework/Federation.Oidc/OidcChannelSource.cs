using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;

namespace TKWF.Federation.Oidc;

/// <summary>
/// OIDC 直配通道静态来源（多通道联邦 v0.3.0 Phase 1——把 <see cref="OidcOptions.Channels"/>
/// （<see cref="OidcPlatformConfig"/> 列表）投影为统一 <see cref="ChannelConfig"/>，供
/// <see cref="StaticChannelRegistry"/> 聚合选区）。
/// <para>投影映射（方案 §3.1 公共列 + Extra）：<c>ChannelId→ChannelId</c> / <c>ClientId→AppId</c> /
/// <c>ClientSecret→AppSecret</c>（公共列——OIDC 系 client_id/client_secret 按 M7 映射约束归一）；
/// 平台特有字段（Platform/AuthorizeUri/TokenUri/UserInfoUri/JwksUri/DiscoveryUri/Scopes/UsePkce/
/// TokenIssuers/FetchUserInfo/ClientAssertionSigningKeyPath）进 Extra（键见 <see cref="OidcChannelConfigKeys"/>；
/// 列表字段 JSON 数组序列化——TokenIssuer 正则可含任意字符，分隔符方案有歧义风险）。
/// <see cref="PlatformType"/> = "oidc"。</para>
/// <para>注册：<c>AddOidcFederationChannel()</c> 内 <c>TryAddEnumerable&lt;IChannelSource&gt;</c>
/// （普通 DI 集合——非 IDomainService 无守卫工厂语义）。凭证自持（Oracle P2-4）不失效，仅投影形态统一。</para>
/// </summary>
public sealed class OidcChannelSource : IChannelSource
{
    private readonly IOptions<OidcOptions> _options;

    /// <summary>构造——Options 注入（读 TKWF:Federation:Oidc 节 + AddOidcFederationChannel 编程追加）。</summary>
    public OidcChannelSource(IOptions<OidcOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string PlatformType => "oidc";

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
                    [OidcChannelConfigKeys.Platform] = c.Platform,
                    [OidcChannelConfigKeys.AuthorizeUri] = c.AuthorizeUri,
                    [OidcChannelConfigKeys.TokenUri] = c.TokenUri,
                    [OidcChannelConfigKeys.UserInfoUri] = c.UserInfoUri,
                    [OidcChannelConfigKeys.JwksUri] = c.JwksUri,
                    [OidcChannelConfigKeys.DiscoveryUri] = c.DiscoveryUri,
                    [OidcChannelConfigKeys.Scopes] = JsonSerializer.Serialize(c.Scopes),
                    [OidcChannelConfigKeys.UsePkce] = c.UsePkce.ToString().ToLowerInvariant(),
                    [OidcChannelConfigKeys.TokenIssuers] = JsonSerializer.Serialize(c.TokenIssuers),
                    [OidcChannelConfigKeys.ClientAssertionSigningKeyPath] = c.ClientAssertionSigningKeyPath,
                    [OidcChannelConfigKeys.FetchUserInfo] = c.FetchUserInfo.ToString().ToLowerInvariant(),
                },
            })
            .ToList();
}
