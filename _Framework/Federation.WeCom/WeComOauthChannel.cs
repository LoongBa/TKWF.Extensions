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
/// 启用且 AgentId 缺 → ctor fail-fast（P1-5，构造即失败不静默降级）。</para>
/// <para>多通道联邦（v0.3.0）：channel 由 <see cref="ChannelConfig"/>（<see cref="ISsoChannelFactory"/> 按 channelId
/// 经 <see cref="IChannelRegistry"/> 预取传入，POCO 非域服务）——<c>null</c> = 集合模板实例（工厂类型索引源，
/// 不直接认证——v0.3.0 多通道）。凭证（CorpId/CorpSecret 法人级标识，方案 M7：WeCom 无标准 AppId——进
/// <see cref="ChannelConfig.Extra"/>，不挤占公共列 <c>AppId</c>）在 <c>AuthenticateAsync</c> 从 cfg.Extra 使用。
/// <see cref="WeComApiClient"/> 不自持凭证解析（gettoken 缓存键 = corpid+":"+corpsecret——P1-2）——通道只传必要参数。</para>
/// <para>边界（Oracle 评审 P2-5）：redirect_uri 一致性校验归装配层（authorize 时记录、回调比对；库不感知，对齐 N3 P1-3）；
/// state 会话绑定归装配层——库 AuthenticateAsync 只收 code。</para>
/// <para>注册：库扩展方法 <c>AddWeComFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92
/// 集合版守卫工厂）；集合元素 = 模板实例（channel=null），真实实例由工厂帧内 <c>ActivatorUtilities</c> 构造
/// （IDomainUser + ChannelConfig 显式传入）。<c>[DiContractIgnore]</c> 豁免 DI001。</para>
/// </summary>
[DiContractIgnore]
public sealed class WeComOauthChannel : DomainServiceBase, ISsoChannel
{
    private readonly WeComApiClient _weComApi;
    private readonly ChannelConfig? _channel;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务）；
    /// <paramref name="channel"/> 为 null = 集合模板实例（工厂类型索引源，不直接认证——v0.3.0 多通道）。
    /// EnableSensitiveInfo 且 AgentId 缺 → fail-fast（P1-5，仅真实实例——模板实例无配置不可校验）。</summary>
    public WeComOauthChannel(
        IDomainUser user,
        WeComApiClient weComApi,
        ChannelConfig? channel = null)
        : base(user)
    {
        _weComApi = weComApi ?? throw new ArgumentNullException(nameof(weComApi));
        _channel = channel;

        // Oracle 评审 P1-5：snsapi_privateinfo 敏感增强须 AgentId（授权必填）——启用且缺 → 构造 fail-fast（不静默降级）
        if (channel is not null
            && IsTrue(channel, "EnableSensitiveInfo")
            && string.IsNullOrEmpty(GetExtra(channel, "AgentId")))
            throw new InvalidOperationException($"企业微信 OAuth channel（{channel.ChannelId}）EnableSensitiveInfo=true 但 AgentId 未配置——snsapi_privateinfo 授权必填 AgentId");
    }

    /// <inheritdoc />
    public string ChannelType => "wecom_oauth";

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
            return new SsoChannelAuthResult(false, null, "WECOM_CODE_REQUIRED", 0);

        // 凭证从 ChannelConfig.Extra 读取（M7：CorpId 法人级标识进 Extra——WeCom 无标准 AppId；CorpSecret 亦进 Extra，
        // 因 WeComApiClient 不自持凭证解析，按缓存键 corpid:corpsecret 使用）
        var corpId = GetExtra(_channel, "CorpId");
        var corpSecret = GetExtra(_channel, "CorpSecret");
        if (string.IsNullOrEmpty(corpId) || string.IsNullOrEmpty(corpSecret))
            return new SsoChannelAuthResult(false, null, "WECOM_CONFIG_MISSING", 0);

        var isThirdParty = IsTrue(_channel, "IsThirdParty");

        try
        {
            // code 直接换身份（无独立 token 端点）——按应用类型选 getuserinfo / getuserinfo3rd
            var identity = await _weComApi.GetIdentityAsync(corpId, corpSecret, code, isThirdParty, ct);

            // external_uid 双策略（Oracle 评审 P0-1）——三方成员 open_userid / 自建成员 {CorpId}:{userid} 复合 / 非成员 openid
            string? externalUserId;
            if (!string.IsNullOrEmpty(identity.OpenUserid))
                externalUserId = identity.OpenUserid;                       // 三方应用成员：全局唯一最佳稳定键
            else if (!string.IsNullOrEmpty(identity.UserId))
                externalUserId = $"{corpId}:{identity.UserId}";             // 自建应用成员：互联企业 CorpId 复合消歧
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

    /// <inheritdoc />
    public Task<SsoChannelAuthorizeResult?> BuildAuthorizeUrlAsync(SsoChannelAuthorizeContext context, CancellationToken ct = default)
    {
        // 模板实例（channel=null）/禁用——不可构造（DIM null 语义 → 端点 AUTHORIZE_NOT_SUPPORTED）
        if (_channel is null || !_channel.IsEnabled)
            return Task.FromResult<SsoChannelAuthorizeResult?>(null);

        // 凭证（CorpId 法人级标识 Extra；缺 → 不可构造——对齐 AuthenticateAsync WECOM_CONFIG_MISSING 语义）
        var corpId = GetExtra(_channel, "CorpId");
        if (string.IsNullOrEmpty(corpId))
            return Task.FromResult<SsoChannelAuthorizeResult?>(null);

        // 双授权流（P1-3 构造归装配层——现收敛于通道）：Parameters["flow"] == "scan" → 扫码（login.work.weixin.qq.com，
        // login_type 自建/三方）；缺省 webview（open.weixin.qq.com #wechat_redirect）
        try
        {
            var agentId = GetExtra(_channel, "AgentId") ?? string.Empty;
            string scope = context.Scope ?? GetExtra(_channel, "DefaultScope") ?? "snsapi_base";
            string state = context.State ?? string.Empty;
            string flow = context.Parameters.TryGetValue("flow", out var f) && !string.IsNullOrEmpty(f) ? f : "webview";

            string url = flow == "scan"
                ? WeComAuthorizeUrlBuilder.BuildScanUrl(corpId, agentId, context.RedirectUri, state,
                    loginType: string.Equals(GetExtra(_channel, "IsThirdParty"), "true", StringComparison.OrdinalIgnoreCase) ? "ServiceApp" : "CorpApp")
                : WeComAuthorizeUrlBuilder.BuildWebviewUrl(corpId, context.RedirectUri, scope, state, agentId);
            return Task.FromResult<SsoChannelAuthorizeResult?>(new SsoChannelAuthorizeResult(url, context.State, null, null));
        }
        catch (ArgumentException)
        {
            // 扫码流 AgentId 缺失等构造参数非法——DIM null 语义（端点 AUTHORIZE_NOT_SUPPORTED）
            return Task.FromResult<SsoChannelAuthorizeResult?>(null);
        }
    }

    private static string? GetExtra(ChannelConfig cfg, string key)
        => cfg.Extra.TryGetValue(key, out var v) ? v : null;

    private static bool IsTrue(ChannelConfig cfg, string key)
        => string.Equals(GetExtra(cfg, key), "true", StringComparison.OrdinalIgnoreCase);
}
