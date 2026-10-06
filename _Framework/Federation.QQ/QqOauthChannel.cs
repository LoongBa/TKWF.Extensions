using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.QQ;

/// <summary>
/// QQ 互联 OAuth 通道（`ISsoChannel: qq_oauth`）——QQ 网页授权身份获取（出站-only 形态，N3 T3）。
/// <para>流程：装配层构造 QQ authorize URL（graph.qq.com/oauth2.0/authorize——client_id + redirect_uri + state 必填
/// + scope 默认 get_user_info + display；**authorize 构造归装配层、通道不感知 state——N3 P1-3**）→ 授权回调带 code →
/// 本通道 <see cref="AuthenticateAsync"/> 经 <see cref="QqApiClient"/> code→access_token（redirect_uri 一致性校验
/// ——N3 P1-4 + OAuth RFC 6749 §4.1.3）→/me→openid（一次性链，用户级 token 不缓存——N3 P1-5）→
/// 返回 <see cref="SsoChannelAuthResult"/>（ExternalUserId=openid，**始终 = openid 稳定映射键——N3 P1-2**），
/// 归一 <c>(channel_id, openid) → uid</c>（编排层/装配层）。AuthLevel=2。</para>
/// <para>注册：库扩展方法 <c>AddQqFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92 集合版守卫工厂）——
/// 集合元素 ctor 注入 IDomainUser 由帧内 CurrentAopUser 供给；channel 业务参数（ChannelId/凭证）经 Options 注入
/// 不占 ctor IDomainUser 槽。<c>[DiContractIgnore]</c>：运行时手写注册豁免 DI001。
/// 本实现继承 <see cref="DomainServiceBase"/>（经基类 <c>User</c> 取上下文——tkwf-extension §4.3 铁律）。</para>
/// </summary>
[DiContractIgnore]
public sealed class QqOauthChannel : DomainServiceBase, ISsoChannel
{
    private readonly QqApiClient _api;
    private readonly QqChannelConfig _channel;

    /// <summary>构造——channel 由配置选区（Channels 首项为默认；多实例按 <see cref="ISsoChannel.ChannelId"/> 选区归后续迭代）。</summary>
    public QqOauthChannel(
        IDomainUser user,
        QqApiClient api,
        IOptions<QqOptions> options)
        : base(user)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _channel = options?.Value.Channels.FirstOrDefault()
            ?? throw new InvalidOperationException("QQ OAuth channel 未配置：TKWF:Federation:QQ 节 Channels 为空");
    }

    /// <inheritdoc />
    public string ChannelType => "qq_oauth";

    /// <inheritdoc />
    public string ChannelId => _channel.ChannelId;

    /// <inheritdoc />
    public async Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (context.Parameters == null
            || !context.Parameters.TryGetValue("code", out var code)
            || string.IsNullOrWhiteSpace(code))
            return new SsoChannelAuthResult(false, null, "QQ_CODE_REQUIRED", 0);

        try
        {
            // redirect_uri 一致性校验（N3 P1-4 — OAuth RFC 6749 §4.1.3）：code 换 token 时 redirect_uri
            // 须与授权时一致，防 code 窃取后换 token 攻击。redirect_uri 由装配层在发起授权时决定并随回调
            // 注入 context（authorize 构造归装配层）——本通道显式传递并交由 QqApiClient 出站比对。
            if (!context.Parameters.TryGetValue("redirect_uri", out var redirectUri)
                || string.IsNullOrWhiteSpace(redirectUri))
                return new SsoChannelAuthResult(false, null, "QQ_REDIRECT_URI_REQUIRED", 0);

            // QQ 换取链（一次性，用户级 token 不缓存 P1-5）：code→access_token→/me→openid
            // external_uid 始终 = openid（应用维度稳定映射键，P1-2）
            var accessToken = await _api.GetAccessTokenAsync(_channel.AppId, code, redirectUri, ct);
            var openId = await _api.GetOpenIdAsync(_channel.AppId, accessToken, ct);
            return new SsoChannelAuthResult(true, openId, null, 2);  // AuthLevel=2 平台便捷
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            // QQ API 业务错误 / 网络错误 / 非 JSON 响应（错误页）——统一失败，FailReason 携带机器可读消息
            return new SsoChannelAuthResult(false, null, ex.Message, 0);
        }
    }
}