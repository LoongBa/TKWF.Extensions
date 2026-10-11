using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信 OAuth 通道（`ISsoChannel: wechat_oauth`）——公众号网页授权身份获取（形态 A/认证服务号）。
/// <para>设计文档 §4.4 形态 A：<c>/sso/login</c> 代理微信 authorize（scope=snsapi_base 静默 / snsapi_userinfo）
/// → 回调带 code → 本通道 <see cref="AuthenticateAsync"/> 经 <see cref="WeChatApiClient.GetOpenIdAsync"/> code 换 openid
/// → 返回 <see cref="SsoChannelAuthResult"/>（ExternalUserId=openid），归一 <c>(channel_id, openid) → uid</c>（编排层/装配层）。
/// 联盟锚点配合：scope=snsapi_base 且 ChannelId=联盟服务号时，openid = 联盟锚点（写 FederationAnchorOpenId 归装配层）。</para>
/// <para>边界（Oracle P2-3）：与认证中心内部 <c>WeChatAuthenticationProvider</c>（服务 token1 签发）区分——
/// 本通道 = 联邦 IdP 适配（服务 token2 联邦流），两会话永不交叉。</para>
/// <para>注册：库扩展方法 <c>AddWeChatFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92 集合版守卫工厂）——
/// 集合元素 ctor 注入 IDomainUser 由帧内 CurrentAopUser 供给；channel 业务参数（ChannelId/凭证）经 Options 注入
/// 不占 ctor IDomainUser 槽（SSO.WeChat §5.2 P1-3 裁定）。<c>[DiContractIgnore]</c>：运行时手写注册豁免 DI001。
/// 本实现继承 <see cref="DomainServiceBase"/>（经基类 <c>User</c> 取上下文——tkwf-extension §4.3 铁律）。</para>
/// </summary>
[DiContractIgnore]
public sealed class WeChatOauthChannel : DomainServiceBase, ISsoChannel
{
    private readonly WeChatApiClient _weChatApi;
    private readonly ChannelConfig? _channel;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务）；
    /// <paramref name="channel"/> 为 null = 集合模板实例（工厂类型索引源，不直接认证——v0.3.0 多通道）。
    /// 凭证（AppId/AppSecret）在 <c>AuthenticateAsync</c> 从 cfg 使用（懒加载——Oracle M1 意图保持）。</summary>
    public WeChatOauthChannel(
        IDomainUser user,
        WeChatApiClient weChatApi,
        ChannelConfig? channel = null)
        : base(user)
    {
        _weChatApi = weChatApi ?? throw new ArgumentNullException(nameof(weChatApi));
        _channel = channel;
    }

    /// <inheritdoc />
    public string ChannelType => "wechat_oauth";

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
            return new SsoChannelAuthResult(false, null, "WECHAT_CODE_REQUIRED", 0);

        try
        {
            // code 是微信 OAuth 授权码（与发起授权的 AppId 绑定）——按本 channel 的 AppId 换 openid
            var openId = await _weChatApi.GetOpenIdAsync(_channel.AppId!, code, ct);
            return new SsoChannelAuthResult(true, openId, null, 2);  // AuthLevel=2 微信便捷
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            // 微信 API 业务错误 / 网络错误 / 非 JSON 响应（错误页）——统一失败，FailReason 携带机器可读消息
            return new SsoChannelAuthResult(false, null, ex.Message, 0);
        }
    }

    /// <inheritdoc />
    public Task<SsoChannelAuthorizeResult?> BuildAuthorizeUrlAsync(SsoChannelAuthorizeContext context, CancellationToken ct = default)
    {
        // 模板实例（channel=null）/禁用——不可构造（DIM null 语义 → 端点 AUTHORIZE_NOT_SUPPORTED）
        if (_channel is null || !_channel.IsEnabled)
            return Task.FromResult<SsoChannelAuthorizeResult?>(null);

        // 公众号网页授权（snsapi_base 静默 / snsapi_userinfo）——state 由 Platform 签名票据传入（回调验签解 app_id/redirect）
        string scope = context.Scope ?? "snsapi_base";
        string url = "https://open.weixin.qq.com/connect/oauth2/authorize"
            + $"?appid={Uri.EscapeDataString(_channel.AppId!)}"
            + $"&redirect_uri={Uri.EscapeDataString(context.RedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(scope)}"
            + $"&state={Uri.EscapeDataString(context.State ?? string.Empty)}"
            + "#wechat_redirect";
        return Task.FromResult<SsoChannelAuthorizeResult?>(new SsoChannelAuthorizeResult(url, context.State, null, null));
    }
}