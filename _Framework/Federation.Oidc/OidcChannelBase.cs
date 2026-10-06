using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.Oidc;

/// <summary>
/// 通用 OIDC 通道基座（M1——国外规划 §五落地：配置驱动 + sub 语义策略，派生平台仅填配置）。
/// <para>实现 <c>ISsoChannel</c>：ChannelType 由派生定义（google_oidc/microsoft_oidc/...）、ChannelId 经
/// <see cref="BuildChannelId"/> 复合编码（Oracle P1-5——pairwise 派生覆写 <c>$"{ChannelType}:{{client_id}}"</c>、
/// public 派生覆写 <c>$"{ChannelType}:*"</c>，构造责任在 channel 内部不依赖消费方手动拼接）。</para>
/// <para>认证流（对齐 WeChat 先例层次）：authorize URL 构造 + state 校验归**装配层**——本通道
/// <see cref="AuthenticateAsync"/> 只收 code（<c>SsoChannelAuthContext.Parameters["code"]</c>），不感知 state；
/// code→token（redirect_uri 一致性 + PKCE code_verifier 经 context 传递）→ id_token JWKS 验签 →
/// <c>SsoChannelAuthResult(ExternalUserId=sub, AuthLevel=2)</c>（sub 不透明字符串——P12）。</para>
/// <para>配置形态：派生 ctor 组装 <see cref="EffectiveConfig"/>（Defaults() 平台默认端点 + 平台 Options 选区合并），
/// 基类不持 config 注入不调虚成员（基类 ctor 虚成员陷阱规避）。</para>
/// <para>注册：<c>TryAddEnumerableConstructible&lt;ISsoChannel, TChannel&gt;</c>（ADR92）——集合元素经守卫工厂
/// 帧内 CurrentAopUser 供给 ctor <see cref="IDomainUser"/> 槽（经基类 User 取上下文）；<c>[DiContractIgnore]</c> 豁免 DI001。</para>
/// </summary>
[DiContractIgnore]
public abstract class OidcChannelBase : DomainServiceBase, ISsoChannel
{
    private readonly OidcChannelFlow _flow;

    protected OidcChannelBase(IDomainUser user, OidcChannelFlow flow)
        : base(user)
    {
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
    }

    /// <inheritdoc />
    public abstract string ChannelType { get; }

    /// <inheritdoc />
    public virtual string ChannelId => BuildChannelId(EffectiveConfig);

    /// <summary>channel_id 复合编码（Oracle P1-5）——默认 config.ChannelId（配置直填）；派生覆写 pairwise/public 形态。</summary>
    protected virtual string BuildChannelId(OidcPlatformConfig config) => config.ChannelId;

    /// <summary>有效平台配置（派生 ctor 组装：<see cref="Defaults"/> + 平台 Options 选区合并）。</summary>
    protected abstract OidcPlatformConfig EffectiveConfig { get; }

    /// <summary>平台默认端点/scope（派生平台库填固定端点——Google/Microsoft/LinkedIn/Slack；直配包装返回空）。</summary>
    protected abstract OidcPlatformConfig Defaults();

    /// <inheritdoc />
    public async Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (context.Parameters == null
            || !context.Parameters.TryGetValue("code", out var code)
            || string.IsNullOrWhiteSpace(code))
            return new SsoChannelAuthResult(false, null, "OIDC_CODE_REQUIRED", 0);

        try
        {
            var config = EffectiveConfig;
            context.Parameters.TryGetValue("redirect_uri", out var redirectUri);   // 装配层授权时生成、回调传递（RFC 6749 §4.1.3 一致性）
            context.Parameters.TryGetValue("code_verifier", out var codeVerifier); // PKCE S256——装配层持有 verifier，回调兑换

            var discovery = !string.IsNullOrWhiteSpace(config.DiscoveryUri)
                ? await _flow.DiscoverAsync(config, ct)
                : null;

            var exchange = await _flow.ExchangeCodeAsync(
                config, discovery, code, redirectUri ?? "", codeVerifier, ct);

            // 信任根正路径抵达 = id_token 验签通过（JWKS RS256 + iss/aud/azp/exp/nbf/sub）——ExternalUserId=sub（不透明字符串 P12）
            // AuthLevel=2 对齐微信 OAuth 便捷档（Oracle P2-1——Federation 编排层按 AuthLevel 决定后续步骤）
            return new SsoChannelAuthResult(true, exchange.IdToken.Sub, null, 2);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or System.Net.Http.HttpRequestException
                                       or System.Text.Json.JsonException)
        {
            // 验签失败/协议错误/网络错误——统一失败，FailReason 携带机器可读消息（信任根负路径：不静默放行）
            return new SsoChannelAuthResult(false, null, ex.Message, 0);
        }
    }

    /// <summary>配置合并——显式配置（Channels 条目）非空字段覆盖平台默认（Defaults）。</summary>
    protected static OidcPlatformConfig MergeConfig(OidcPlatformConfig defaults, OidcPlatformConfig? overrideCfg)
    {
        if (overrideCfg is null)
            return defaults;

        return new OidcPlatformConfig
        {
            ChannelId = !string.IsNullOrWhiteSpace(overrideCfg.ChannelId) ? overrideCfg.ChannelId : defaults.ChannelId,
            Platform = !string.IsNullOrWhiteSpace(overrideCfg.Platform) ? overrideCfg.Platform : defaults.Platform,
            ClientId = !string.IsNullOrWhiteSpace(overrideCfg.ClientId) ? overrideCfg.ClientId : defaults.ClientId,
            ClientSecret = !string.IsNullOrWhiteSpace(overrideCfg.ClientSecret) ? overrideCfg.ClientSecret : defaults.ClientSecret,
            AuthorizeUri = !string.IsNullOrWhiteSpace(overrideCfg.AuthorizeUri) ? overrideCfg.AuthorizeUri : defaults.AuthorizeUri,
            TokenUri = !string.IsNullOrWhiteSpace(overrideCfg.TokenUri) ? overrideCfg.TokenUri : defaults.TokenUri,
            UserInfoUri = !string.IsNullOrWhiteSpace(overrideCfg.UserInfoUri) ? overrideCfg.UserInfoUri : defaults.UserInfoUri,
            JwksUri = !string.IsNullOrWhiteSpace(overrideCfg.JwksUri) ? overrideCfg.JwksUri : defaults.JwksUri,
            DiscoveryUri = !string.IsNullOrWhiteSpace(overrideCfg.DiscoveryUri) ? overrideCfg.DiscoveryUri : defaults.DiscoveryUri,
            Scopes = overrideCfg.Scopes.Count > 0 ? overrideCfg.Scopes : defaults.Scopes,
            UsePkce = overrideCfg.UsePkce,
            TokenIssuers = overrideCfg.TokenIssuers.Count > 0 ? overrideCfg.TokenIssuers : defaults.TokenIssuers,
            ClientAssertionSigningKeyPath = !string.IsNullOrWhiteSpace(overrideCfg.ClientAssertionSigningKeyPath)
                ? overrideCfg.ClientAssertionSigningKeyPath : defaults.ClientAssertionSigningKeyPath,
            FetchUserInfo = overrideCfg.FetchUserInfo,
        };
    }

    /// <summary>直配场景选区（Channels.FirstOrDefault——对齐 WeChat 先例 Channels[0] 默认，多实例选区归后续迭代）。</summary>
    protected static OidcPlatformConfig? FirstConfigured(IOptions<OidcOptions> options)
        => options?.Value.Channels?.Count > 0 ? options.Value.Channels[0] : null;
}