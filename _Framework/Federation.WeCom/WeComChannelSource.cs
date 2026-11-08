using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using TKWF.Ext.Federation;

namespace TKWF.Federation.WeCom;

/// <summary>
/// 企业微信静态通道来源（多通道联邦 v0.3.0 Phase 1——把 <see cref="WeComOptions.Channels"/> 投影为统一
/// <see cref="ChannelConfig"/>，供 <c>StaticChannelRegistry</c> 聚合选区）。
/// <para>投影映射（方案 §3.1 公共列 + Extra + M7 约束）：<c>ChannelId→ChannelId</c>（公共列）；<b>全部平台特有
/// 字段进 Extra</b>——<c>CorpId</c>（法人级标识——WeCom 无标准 AppId，公共列 <c>AppId</c> 不承载；M7：法人级
/// 标识一律进 Extra 不挤占 AppId）/ <c>CorpSecret</c>（WeComApiClient 不自持凭证解析——gettoken 缓存键
/// corpid:corpsecret P1-2，公共列 <c>AppSecret</c> 不适配）/ <c>AgentId</c>/<c>IsThirdParty</c>/
/// <c>EnableSensitiveInfo</c>/<c>Token</c>/<c>EncodingAESKey</c>（入站信任根）/ <c>DefaultScope</c>（Options 级兜底，
/// 投影到每通道 Extra——装配层构造 authorize URL 兜底）。<see cref="PlatformType"/> = "wecom"。</para>
/// <para>注册：<c>AddWeComFederationChannels()</c> 内 <c>TryAddEnumerable&lt;IChannelSource&gt;</c>
/// （普通 DI 集合——非 IDomainService 无守卫工厂语义）。凭证自持（Oracle P2-4）不失效，仅投影形态统一。</para>
/// </summary>
public sealed class WeComChannelSource : IChannelSource
{
    private readonly IOptions<WeComOptions> _options;

    /// <summary>构造——Options 注入（读 TKWF:Federation:WeCom 节）。</summary>
    public WeComChannelSource(IOptions<WeComOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string PlatformType => "wecom";

    /// <summary>能力声明（I2-2 §3.5——企业微信双通道实际能力面）：<c>OAuth | Event</c>——wecom_oauth（AuthenticateAsync）
    /// + wecom_event（I2-2 起 HandleEventAsync 真实解析产出 ChannelEvent + VerifyUrlAsync echostr 握手）。
    /// <para><b>不声明</b> <c>Follow</c>/<c>FollowQuery</c>——企微关注应用事件属成员关系非"公众号关注"订阅语义
    /// （subscribe/unsubscribe 原文透传不翻关注状态，P1-3）、反查未立项（YAGNI）。</para></summary>
    public ChannelCapabilities Capabilities => ChannelCapabilities.OAuth | ChannelCapabilities.Event;

    /// <inheritdoc />
    public IReadOnlyList<ChannelConfig> GetChannels()
    {
        var defaultScope = _options.Value.DefaultScope;
        return _options.Value.Channels
            .Select(c => new ChannelConfig
            {
                ChannelId = c.ChannelId,
                PlatformType = PlatformType,
                Alias = c.Alias,   // 对外别名（可空——缺省 null = 用 ChannelId 对外，方案 §3.7 双键）
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["CorpId"] = c.CorpId,
                    ["CorpSecret"] = c.CorpSecret,
                    ["AgentId"] = c.AgentId,
                    ["IsThirdParty"] = c.IsThirdParty ? "true" : "false",
                    ["EnableSensitiveInfo"] = c.EnableSensitiveInfo ? "true" : "false",
                    ["Token"] = c.Token,
                    ["EncodingAESKey"] = c.EncodingAESKey,
                    ["DefaultScope"] = defaultScope,
                },
            })
            .ToList();
    }
}
