using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.QQ;

/// <summary>
/// QQ 互联 OAuth 通道（`ISsoChannel: qq_oauth`）——QQ 网页授权身份获取（出站-only 形态，N3 T3 + 多通道联邦 v0.3.0）。
/// <para>流程：装配层构造 QQ authorize URL（graph.qq.com/oauth2.0/authorize——client_id + redirect_uri + state 必填
/// + scope 默认 get_user_info + display；**authorize 构造归装配层、通道不感知 state——N3 P1-3**）→ 授权回调带 code →
/// 本通道 <see cref="AuthenticateAsync"/> 经 <see cref="QqApiClient"/> code→access_token（redirect_uri 一致性校验
/// ——N3 P1-4 + OAuth RFC 6749 §4.1.3）→/me→openid（一次性链，用户级 token 不缓存——N3 P1-5）→
/// 返回 <see cref="SsoChannelAuthResult"/>（ExternalUserId=openid，**始终 = openid 稳定映射键——N3 P1-2**），
/// 归一 <c>(channel_id, openid) → uid</c>（编排层/装配层）。AuthLevel=2。</para>
/// <para><b>多通道联邦（v0.3.0 选区机制，Oracle M1/M2/M8）</b>：ctor 收 <see cref="ChannelConfig"/>
/// （<see cref="ISsoChannelFactory"/> 预取传入——POCO 非域服务规避 DI004 与 DomainHost 依赖）；凭证
/// （AppId/AppSecret/EnableUnionId）在 <c>AuthenticateAsync</c> 从 <see cref="ChannelConfig"/> 使用
/// （EnableUnionId 经 Extra 承载——QQ 特有开关，N3 §3.3）。<paramref name="channel"/> 为 null = 集合模板实例
/// （工厂类型索引源，不直接认证）。</para>
/// <para>注册：库扩展方法 <c>AddQqFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92 集合版守卫工厂）+
/// <see cref="QqChannelSource"/>（IChannelSource——静态配置投影统一 ChannelConfig）。</para>
/// </summary>
[DiContractIgnore]
public sealed class QqOauthChannel : DomainServiceBase, ISsoChannel
{
    private readonly QqApiClient _api;
    private readonly ChannelConfig? _channel;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入）；null = 集合模板实例。</summary>
    public QqOauthChannel(
        IDomainUser user,
        QqApiClient api,
        ChannelConfig? channel = null)
        : base(user)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _channel = channel;
    }

    /// <inheritdoc />
    public string ChannelType => "qq_oauth";

    /// <inheritdoc />
    public string ChannelId => _channel?.ChannelId ?? "";

    /// <inheritdoc />
    public async Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (_channel is null)
            return new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0);   // 模板实例不可直接认证
        if (!_channel.IsEnabled)
            return new SsoChannelAuthResult(false, null, "CHANNEL_DISABLED", 0);

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
            var accessToken = await _api.GetAccessTokenAsync(_channel.AppId!, code, redirectUri, ct);
            var openId = await _api.GetOpenIdAsync(_channel.AppId!, accessToken, ct);
            return new SsoChannelAuthResult(true, openId, null, 2);  // AuthLevel=2 平台便捷
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            // QQ API 业务错误 / 网络错误 / 非 JSON 响应（错误页）——统一失败，FailReason 携带机器可读消息
            return new SsoChannelAuthResult(false, null, ex.Message, 0);
        }
    }

    /// <inheritdoc />
    public Task<SsoChannelAuthorizeResult?> BuildAuthorizeUrlAsync(SsoChannelAuthorizeContext context, CancellationToken ct = default)
    {
        // 模板实例（channel=null）/禁用——不可构造（DIM null 语义 → 端点 AUTHORIZE_NOT_SUPPORTED）
        if (_channel is null || !_channel.IsEnabled)
            return Task.FromResult<SsoChannelAuthorizeResult?>(null);

        // QQ 网页授权（state 必填——Platform 签名票据；scope 默认 get_user_info；display 可经 Parameters 扩展）
        string scope = context.Scope ?? "get_user_info";
        string display = context.Parameters.TryGetValue("display", out var d) && !string.IsNullOrEmpty(d)
            ? $"&display={Uri.EscapeDataString(d)}" : string.Empty;
        string url = "https://graph.qq.com/oauth2.0/authorize"
            + $"?client_id={Uri.EscapeDataString(_channel.AppId!)}"
            + $"&redirect_uri={Uri.EscapeDataString(context.RedirectUri)}"
            + $"&state={Uri.EscapeDataString(context.State ?? string.Empty)}"
            + $"&scope={Uri.EscapeDataString(scope)}"
            + display;
        return Task.FromResult<SsoChannelAuthorizeResult?>(new SsoChannelAuthorizeResult(url, context.State, null, null));
    }
}