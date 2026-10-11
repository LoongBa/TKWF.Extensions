using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.DingTalk;

/// <summary>
/// 钉钉 OAuth 通道（`ISsoChannel: dingtalk_oauth`）——钉钉新 OAuth2 网页授权身份获取（扫码内嵌/跳转）。
/// <para>流程（M3 方案 §3.1）：装配层构造 authorize URL（login.dingtalk.com/oauth2/auth？
/// client_id+redirect_uri+scope=openid+prompt=consent+state——DTFrameLogin iframe 内嵌或 302 跳转）
/// → 钉钉授权 → 回调 ?code=&amp;state= → 本通道 <see cref="AuthenticateAsync"/> 经
/// <see cref="DingTalkApiClient.GetUserAccessTokenAsync"/> code 换用户级 token → openId/unionId/corpId
/// → 返回 <see cref="SsoChannelAuthResult"/>（ExternalUserId=<b>openId</b>——external_uid 恒 = openId，
/// 应用维度稳定映射键；unionId 写联盟锚点辅助归装配层）。</para>
/// <para>两步流（Oracle 评审 P1-4）：channel 只返回 <c>SsoChannelAuthResult(ExternalUserId=openId)</c>
/// （契约无 userid 承载字段）；userid（企业维度员工 id）由装配层按需调
/// <see cref="DingTalkApiClient.GetUserIdAsync"/> 经 <see cref="DingTalkUserInfo"/> 承载——非 external_uid。</para>
/// <para>边界（Oracle 评审 P1-3）：钉钉 token 端点（userAccessToken）<b>不接受 redirect_uri</b>——
/// redirect_uri 仅在 authorize 回调用，一致性校验归装配层（state 会话绑定 + code 一次性 +
/// clientSecret 对称换取）。</para>
/// <para>注册：库扩展方法 <c>AddDingTalkFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92
/// 集合版守卫工厂）——集合元素 ctor 注入 IDomainUser 由帧内 CurrentAopUser 供给；channel 业务参数
/// （ChannelId/凭证）经工厂预取的 <see cref="ChannelConfig"/> 传入（多通道联邦 v0.3.0——不占 ctor
/// IDomainUser 槽）。<c>[DiContractIgnore]</c>：运行时手写注册豁免 DI001。</para>
/// </summary>
[DiContractIgnore]
public sealed class DingTalkOauthChannel : DomainServiceBase, ISsoChannel
{
    private readonly DingTalkApiClient _dingTalkApi;
    private readonly ChannelConfig? _channel;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务）；
    /// <paramref name="channel"/> 为 null = 集合模板实例（工厂类型索引源，不直接认证——v0.3.0 多通道）。
    /// 凭证（AppKey——钉钉凭证解析键；AppSecret 公共列由 <see cref="DingTalkApiClient"/> 自解析）
    /// 在 <c>AuthenticateAsync</c> 从 cfg 使用（懒加载——Oracle M1 意图保持）。</summary>
    public DingTalkOauthChannel(
        IDomainUser user,
        DingTalkApiClient dingTalkApi,
        ChannelConfig? channel = null)
        : base(user)
    {
        _dingTalkApi = dingTalkApi ?? throw new ArgumentNullException(nameof(dingTalkApi));
        _channel = channel;
    }

    /// <inheritdoc />
    public string ChannelType => "dingtalk_oauth";

    /// <inheritdoc />
    public string ChannelId => _channel?.ChannelId ?? "";

    /// <inheritdoc />
    public async Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (_channel is null)
            return new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0);   // 模板实例不可直接认证
        if (!_channel.IsEnabled)
            return new SsoChannelAuthResult(false, null, "CHANNEL_DISABLED", 0);

        // 平台特定负载（Extra——AppKey 钉钉凭证解析键；AppSecret 公共列由 DingTalkApiClient 自解析）
        _channel.Extra.TryGetValue("AppKey", out var appKey);
        if (string.IsNullOrEmpty(appKey))
            return new SsoChannelAuthResult(false, null, "DINGTALK_APPKEY_MISSING", 0);

        if (context.Parameters == null
            || !context.Parameters.TryGetValue("code", out var code)
            || string.IsNullOrWhiteSpace(code))
            return new SsoChannelAuthResult(false, null, "DINGTALK_CODE_REQUIRED", 0);

        try
        {
            // code 是钉钉 OAuth 授权码（与发起授权的 AppKey 绑定）——按本 channel 的 AppKey 换用户级 token
            var token = await _dingTalkApi.GetUserAccessTokenAsync(appKey!, code, ct);
            if (string.IsNullOrEmpty(token.OpenId))
                return new SsoChannelAuthResult(false, null, "DINGTALK_OPENID_MISSING", 0);

            // external_uid 恒 = openId（应用维度稳定标识）；unionId 锚点辅助归装配层（N2）
            return new SsoChannelAuthResult(true, token.OpenId, null, 2);  // AuthLevel=2 对齐微信便捷语义
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException)
        {
            // 钉钉 API 业务错误 / 网络错误 / 非 JSON 响应（错误页）——统一失败，FailReason 携带机器可读消息
            return new SsoChannelAuthResult(false, null, ex.Message, 0);
        }
    }

    /// <inheritdoc />
    public Task<SsoChannelAuthorizeResult?> BuildAuthorizeUrlAsync(SsoChannelAuthorizeContext context, CancellationToken ct = default)
    {
        // 模板实例（channel=null）/禁用——不可构造（DIM null 语义 → 端点 AUTHORIZE_NOT_SUPPORTED）
        if (_channel is null || !_channel.IsEnabled)
            return Task.FromResult<SsoChannelAuthorizeResult?>(null);

        // 凭证（AppKey——钉钉凭证解析键，Extra 承载；缺 → 不可构造）
        if (!_channel.Extra.TryGetValue("AppKey", out var appKey) || string.IsNullOrEmpty(appKey))
            return Task.FromResult<SsoChannelAuthorizeResult?>(null);

        // 钉钉新 OAuth2（login.dingtalk.com/oauth2/auth——scope 默认 openid（或 "openid corpid" 经 Parameters["scope"]）；
        // prompt=consent 必填；iframe 内嵌须与 redirect_uri 同源归装配层）
        string scope = context.Scope ?? "openid";
        string url = "https://login.dingtalk.com/oauth2/auth"
            + $"?client_id={Uri.EscapeDataString(appKey)}"
            + $"&redirect_uri={Uri.EscapeDataString(context.RedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(scope)}"
            + "&prompt=consent"
            + $"&state={Uri.EscapeDataString(context.State ?? string.Empty)}";
        return Task.FromResult<SsoChannelAuthorizeResult?>(new SsoChannelAuthorizeResult(url, context.State, null, null));
    }
}
