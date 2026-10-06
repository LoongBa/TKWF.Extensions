using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.WeCom;

/// <summary>
/// 企业微信 OAuth 通道（`ISsoChannel: wecom_oauth`）——双授权流统一身份获取。
/// <para>流程（M4 方案 §3.1）：装配层构造 authorize URL（<see cref="WeComAuthorizeUrlBuilder"/>——双授权流
/// Webview <c>#wechat_redirect</c> vs 扫码 <c>login_type</c>，Oracle 评审 P1-3）→ 企业微信授权（Webview 跳转/扫码）
/// → 回调 ?code=&amp;state=（域名须完全匹配可信域名否则 50001）→ 本通道 <see cref="AuthenticateAsync"/> 经
/// <see cref="WeComApiClient.GetIdentityAsync"/>（gettoken 企业 access_token + getuserinfo/getuserinfo3rd）
/// code 直接换身份（<b>无独立 token 端点</b>）→ <b>external_uid 双策略（Oracle 评审 P0-1）</b>：
/// 三方成员 <c>open_userid</c>（全局唯一）/ 自建成员 <c>{CorpId}:{userid}</c> 复合 / 非成员 <c>openid</c>。</para>
/// <para>敏感信息（Oracle 评审 P1-4/P1-5）：<c>EnableSensitiveInfo</c> 启用时 <see cref="WeComApiClient.GetIdentityAsync"/>
/// 条件性透出 <c>UserTicket</c>（自建 96442 + 三方 98179 返回；扫码 98177 不返回降级仅身份）——<b>channel 内不拉取
/// 敏感信息</b>（数据流闭环归装配层：编排层按需 <see cref="WeComApiClient.GetSensitiveInfoAsync"/> 即用即弃不落库）；
/// 启用且 <see cref="WeComChannelConfig.AgentId"/> 缺 → ctor fail-fast（P1-5，启动即失败不静默降级）。</para>
/// <para>边界（Oracle 评审 P2-5）：redirect_uri 一致性校验归装配层（authorize 时记录、回调比对；库不感知，对齐 N3 P1-3）；
/// state 会话绑定归装配层——库 AuthenticateAsync 只收 code。</para>
/// <para>注册：库扩展方法 <c>AddWeComFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92
/// 集合版守卫工厂）；channel 业务参数经 Options 注入不占 ctor IDomainUser 槽。<c>[DiContractIgnore]</c> 豁免 DI001。</para>
/// </summary>
[DiContractIgnore]
public sealed class WeComOauthChannel : DomainServiceBase, ISsoChannel
{
    private readonly WeComApiClient _weComApi;
    private readonly WeComChannelConfig _channel;

    /// <summary>构造——channel 由配置选区（Channels 首项为默认应用）；EnableSensitiveInfo 且 AgentId 缺 → fail-fast（P1-5）。</summary>
    public WeComOauthChannel(
        IDomainUser user,
        WeComApiClient weComApi,
        IOptions<WeComOptions> options)
        : base(user)
    {
        _weComApi = weComApi ?? throw new ArgumentNullException(nameof(weComApi));
        _channel = options?.Value.Channels.FirstOrDefault()
            ?? throw new InvalidOperationException("企业微信 OAuth channel 未配置：TKWF:Federation:WeCom 节 Channels 为空");

        // Oracle 评审 P1-5：snsapi_privateinfo 敏感增强须 AgentId（授权必填）——启用且缺 → 启动 fail-fast（不静默降级）
        if (_channel.EnableSensitiveInfo && string.IsNullOrEmpty(_channel.AgentId))
            throw new InvalidOperationException($"企业微信 OAuth channel（{_channel.ChannelId}）EnableSensitiveInfo=true 但 AgentId 未配置——snsapi_privateinfo 授权必填 AgentId");
    }

    /// <inheritdoc />
    public string ChannelType => "wecom_oauth";

    /// <inheritdoc />
    public string ChannelId => _channel.ChannelId;

    /// <inheritdoc />
    public async Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (context.Parameters == null
            || !context.Parameters.TryGetValue("code", out var code)
            || string.IsNullOrWhiteSpace(code))
            return new SsoChannelAuthResult(false, null, "WECOM_CODE_REQUIRED", 0);

        try
        {
            // code 直接换身份（无独立 token 端点）——按应用类型选 getuserinfo / getuserinfo3rd
            var identity = await _weComApi.GetIdentityAsync(_channel.CorpId, _channel.CorpSecret, code, _channel.IsThirdParty, ct);

            // external_uid 双策略（Oracle 评审 P0-1）——三方成员 open_userid / 自建成员 {CorpId}:{userid} 复合 / 非成员 openid
            string? externalUserId;
            if (!string.IsNullOrEmpty(identity.OpenUserid))
                externalUserId = identity.OpenUserid;                       // 三方应用成员：全局唯一最佳稳定键
            else if (!string.IsNullOrEmpty(identity.UserId))
                externalUserId = $"{_channel.CorpId}:{identity.UserId}";    // 自建应用成员：互联企业 CorpId 复合消歧
            else if (!string.IsNullOrEmpty(identity.OpenId))
                externalUserId = identity.OpenId;                           // 非企业成员：对当前企业唯一
            else
                return new SsoChannelAuthResult(false, null, "WECOM_IDENTITY_MISSING", 0);

            // 敏感信息增强（P1-4）：UserTicket 已由 GetIdentityAsync 透出（EnableSensitiveInfo 时）——channel 不拉取，
            // 数据流闭环归装配层（编排层按需 GetSensitiveInfoAsync 即用即弃）；扫码场景降级仅身份
            return new SsoChannelAuthResult(true, externalUserId, null, 2);  // AuthLevel=2 对齐微信便捷语义
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException)
        {
            // 企业微信 API 业务错误 / 网络错误 / 非 JSON 响应（错误页）——统一失败，FailReason 携带机器可读消息
            return new SsoChannelAuthResult(false, null, ex.Message, 0);
        }
    }
}
