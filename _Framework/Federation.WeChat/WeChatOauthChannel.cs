using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

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
    private readonly WeChatChannelConfig _channel;

    /// <summary>构造——channel 由配置选区（Channels 首项为默认公众号；多公众号按 <see cref="ISsoChannel.ChannelId"/>
    /// 选区——当前 v0.1.0 取 Channels[0] 默认，多实例选区增强归后续迭代）。</summary>
    public WeChatOauthChannel(
        IDomainUser user,
        WeChatApiClient weChatApi,
        IOptions<WeChatOptions> options)
        : base(user)
    {
        _weChatApi = weChatApi ?? throw new ArgumentNullException(nameof(weChatApi));
        _channel = options?.Value.Channels.FirstOrDefault()
            ?? throw new InvalidOperationException("微信 OAuth channel 未配置：TKWF:Federation:WeChat 节 Channels 为空");
    }

    /// <inheritdoc />
    public string ChannelType => "wechat_oauth";

    /// <inheritdoc />
    public string ChannelId => _channel.ChannelId;

    /// <inheritdoc />
    public async Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (context.Parameters == null
            || !context.Parameters.TryGetValue("code", out var code)
            || string.IsNullOrWhiteSpace(code))
            return new SsoChannelAuthResult(false, null, "WECHAT_CODE_REQUIRED", 0);

        try
        {
            // code 是微信 OAuth 授权码（与发起授权的 AppId 绑定）——按本 channel 的 AppId 换 openid
            var openId = await _weChatApi.GetOpenIdAsync(_channel.AppId, code, ct);
            return new SsoChannelAuthResult(true, openId, null, 2);  // AuthLevel=2 微信便捷
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            // 微信 API 业务错误 / 网络错误 / 非 JSON 响应（错误页）——统一失败，FailReason 携带机器可读消息
            return new SsoChannelAuthResult(false, null, ex.Message, 0);
        }
    }
}