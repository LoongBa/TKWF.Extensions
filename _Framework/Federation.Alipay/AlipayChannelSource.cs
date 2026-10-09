using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;

namespace TKWF.Federation.Alipay;

/// <summary>
/// 支付宝静态通道来源（多通道联邦 v0.3.0 Phase 1——把 <see cref="AlipayOptions.Channels"/> 投影为统一
/// <see cref="ChannelConfig"/>，供 <see cref="StaticChannelRegistry"/> 聚合选区，组件 8.5）。
/// <para>投影映射（方案 §3.1 公共列 + Extra）：<c>ChannelId→ChannelId</c> / <c>AppId→AppId</c>（公共列）；
/// <c>AppSecret = null</c>（⚠️ 支付宝无对称应用密钥——RSA2 双向签名，非对称/对称分离）；
/// 平台特有凭证进 Extra：<c>PrivateKeyPath / AlipayPublicKeyPath / EnableMobile</c>（RSA 私钥路径进 Extra 非
/// AppSecret 列——方案 F5 零表结构变更）。AlipayApiClient 凭证解析机制保留（Options 按 AppId 精确匹配），
/// 通道只传 AppId。</para>
/// <para>注册：<c>AddAlipayFederationChannels()</c> 内 <c>TryAddEnumerable&lt;IChannelSource&gt;</c>
/// （普通 DI 集合——非 IDomainService 无守卫工厂语义）。凭证自持（Oracle P2-4）不失效，仅投影形态统一。</para>
/// </summary>
public sealed class AlipayChannelSource : IChannelSource
{
    private readonly IOptions<AlipayOptions> _options;

    /// <summary>构造——Options 注入（读 TKWF:Federation:Alipay 节）。</summary>
    public AlipayChannelSource(IOptions<AlipayOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string PlatformType => "alipay";

    /// <inheritdoc />
    public IReadOnlyList<ChannelConfig> GetChannels()
        => _options.Value.Channels
            .Select(c => new ChannelConfig
            {
                ChannelId = c.ChannelId,
                PlatformType = PlatformType,
                Alias = c.Alias,   // 对外别名（可空——缺省 null = 用 ChannelId 对外，方案 §3.7 双键）
                AppId = c.AppId,
                AppSecret = null, // 支付宝无对称密钥——RSA 私钥路径进 Extra（非对称/对称分离，方案 F5 零表结构变更）
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["PrivateKeyPath"] = c.PrivateKeyPath,
                    ["AlipayPublicKeyPath"] = c.AlipayPublicKeyPath,
                    ["EnableMobile"] = c.EnableMobile.ToString(),
                },
            })
            .ToList();
}
