using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.Oidc;

/// <summary>
/// 通用 OIDC 通道基座（M1——国外规划 §五落地：配置驱动 + sub 语义策略，派生平台仅填配置；
/// v0.3.0 多通道联邦——通道 ctor 收 <see cref="ChannelConfig"/>（工厂预取，POCO 非域服务规避 DI004）。
/// <para>实现 <c>ISsoChannel</c>：ChannelType 由派生定义（google_oidc/microsoft_oidc/oidc_oidc...）、ChannelId 经
/// <see cref="BuildChannelId"/> 复合编码（Oracle P1-5——pairwise 派生覆写 <c>$"{ChannelType}:{{client_id}}"</c>、
/// public 派生覆写 <c>$"{ChannelType}:*"</c>，构造责任在 channel 内部不依赖消费方手动拼接）。</para>
/// <para>认证流（对齐 WeChat 先例层次）：authorize URL 构造 + state 校验归**装配层**——本通道
/// <see cref="AuthenticateAsync"/> 只收 code（<c>SsoChannelAuthContext.Parameters["code"]</c>），不感知 state；
/// code→token（redirect_uri 一致性 + PKCE code_verifier 经 context 传递）→ id_token JWKS 验签 →
/// <c>SsoChannelAuthResult(ExternalUserId=sub, AuthLevel=2)</c>（sub 不透明字符串——P12）。</para>
/// <para><b>配置形态（v0.3.0 多通道）</b>：通道 ctor 收工厂预取的 <see cref="ChannelConfig"/>（凭证/平台特有负载
/// 在 <c>Extra</c>——OIDC 系 ClientId→公共列 <c>AppId</c>、ClientSecret→<c>AppSecret</c>（M7 约束）；
/// 端点/scope/issuer 等平台特有字段进 <c>Extra</c>（键见 <see cref="OidcChannelConfigKeys"/>）。
/// <see cref="EffectiveConfig"/> = <c>FromChannel(_channel)</c>（Extra 重建 <see cref="OidcPlatformConfig"/>）+
/// <see cref="Defaults"/>（派生平台固定端点）合并——受保护虚方法语义保留（派生 BuildChannelId 仍可用）；
/// <c>_channel is null</c> = 集合模板实例（工厂类型索引源，不直接认证——返回 CHANNEL_NOT_FOUND/CHANNEL_DISABLED）。</para>
/// <para>注册：<c>TryAddEnumerableConstructible&lt;ISsoChannel, TChannel&gt;</c>（ADR92）——集合元素经守卫工厂
/// 帧内 CurrentAopUser 供给 ctor <see cref="IDomainUser"/> 槽（经基类 User 取上下文）；<c>[DiContractIgnore]</c> 豁免 DI001。</para>
/// </summary>
[DiContractIgnore]
public abstract class OidcChannelBase : DomainServiceBase, ISsoChannel
{
    private readonly OidcChannelFlow _flow;
    private OidcPlatformConfig? _effective;

    /// <summary>通道配置（工厂按 ChannelId 预取传入；<c>null</c> = 集合模板实例）。</summary>
    protected readonly ChannelConfig? _channel;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务，ActivatorUtilities
    /// 经 ctor 显式参数供给；<c>null</c> = 模板实例）。流（<see cref="OidcChannelFlow"/>）为 typed client DI 解析。</summary>
    protected OidcChannelBase(IDomainUser user, OidcChannelFlow flow, ChannelConfig? channel = null)
        : base(user)
    {
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _channel = channel;
    }

    /// <inheritdoc />
    public abstract string ChannelType { get; }

    /// <inheritdoc />
    public virtual string ChannelId => BuildChannelId(EffectiveConfig);

    /// <summary>channel_id 复合编码（Oracle P1-5）——默认 config.ChannelId（配置直填）；派生覆写 pairwise/public 形态。</summary>
    protected virtual string BuildChannelId(OidcPlatformConfig config) => config.ChannelId;

    /// <summary>
    /// 有效平台配置（v0.3.0 多通道联邦语义）——默认实现 = <c>FromChannel(_channel)</c>（Extra 重建）+
    /// <see cref="Defaults"/> 合并（受保护虚方法——派生可覆写定制组装，如 Microsoft tenant 模板端点）。
    /// <c>_channel is null</c>（模板实例）返回 <see cref="Defaults"/>（BuildChannelId 可计算，不认证）。
    /// </summary>
    protected virtual OidcPlatformConfig EffectiveConfig
        => _channel is null ? Defaults() : _effective ??= MergeConfig(Defaults(), FromChannel(_channel));

    /// <summary>平台默认端点/scope（派生平台库填固定端点——Google/Microsoft/LinkedIn/Slack；直配包装返回空）。</summary>
    protected abstract OidcPlatformConfig Defaults();

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

    /// <summary>配置合并——显式配置（FromChannel 重建结果）非空字段覆盖平台默认（Defaults）。</summary>
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

    /// <summary>
    /// 从 <see cref="ChannelConfig"/>（工厂预取）重建 <see cref="OidcPlatformConfig"/>——多通道联邦 v0.3.0
    /// 的 Extra 解码端（与各库 <c>XxxChannelSource</c> 投影对称）。
    /// <para>映射：公共列 <c>AppId→ClientId</c> / <c>AppSecret→ClientSecret</c>（OIDC 系 client_id/client_secret 按 M7
    /// 归一到公共列）；平台特有字段经 <see cref="OidcChannelConfigKeys"/> 从 <c>Extra</c> 读取
    /// （列表字段 JSON 数组序列化——TokenIssuer 正则可含任意字符，分隔符方案有歧义风险）。
    /// 列表缺省为空（MergeConfig 回退平台 Defaults）；UsePkce 缺省 true（OidcPlatformConfig 默认值对齐）。</para>
    /// </summary>
    protected static OidcPlatformConfig FromChannel(ChannelConfig channel)
    {
        var extra = channel.Extra;
        string Get(string key) => extra.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : "";
        bool GetBool(string key) => extra.TryGetValue(key, out var v) && bool.TryParse(v, out var b) && b;
        bool? GetBoolOrNull(string key)
            => extra.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : null;

        return new OidcPlatformConfig
        {
            ChannelId = channel.ChannelId,
            Platform = Get(OidcChannelConfigKeys.Platform),
            ClientId = channel.AppId ?? "",
            ClientSecret = channel.AppSecret ?? "",
            AuthorizeUri = Get(OidcChannelConfigKeys.AuthorizeUri),
            TokenUri = Get(OidcChannelConfigKeys.TokenUri),
            UserInfoUri = Get(OidcChannelConfigKeys.UserInfoUri),
            JwksUri = Get(OidcChannelConfigKeys.JwksUri),
            DiscoveryUri = Get(OidcChannelConfigKeys.DiscoveryUri),
            Scopes = DeserializeList(Get(OidcChannelConfigKeys.Scopes)),
            UsePkce = GetBoolOrNull(OidcChannelConfigKeys.UsePkce) ?? true,
            TokenIssuers = DeserializeList(Get(OidcChannelConfigKeys.TokenIssuers)),
            ClientAssertionSigningKeyPath = Get(OidcChannelConfigKeys.ClientAssertionSigningKeyPath),
            FetchUserInfo = GetBool(OidcChannelConfigKeys.FetchUserInfo),
        };
    }

    /// <summary>读取自身 <c>_channel.Extra</c> 字段（模板实例/缺失 → fallback）——派生覆写 EffectiveConfig 组装用。</summary>
    protected string? GetChannelValue(string key, string? fallback = null)
        => _channel is not null && _channel.Extra.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : fallback;

    /// <summary>Extra 列表字段解码（JSON 数组——TokenIssuer 正则可含任意字符，JSON 防歧义）。</summary>
    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json!) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
/// OIDC 平台特有字段在 <see cref="ChannelConfig.Extra"/> 中的键（多通道联邦 v0.3.0——OIDC 系无标准
/// AppId/AppSecret 命名：ClientId/ClientSecret 走公共列 <c>AppId</c>/<c>AppSecret</c>（M7 映射约束），
/// 其余平台特有字段全进 Extra；与各库 <c>XxxChannelSource</c> 投影 + <see cref="OidcChannelBase.FromChannel"/>
/// 解码对称——单一事实源防 typo 静默解析错键）。
/// <para>列表字段（<see cref="Scopes"/>/<see cref="TokenIssuers"/>）以 <b>JSON 数组序列化</b>承载
/// （TokenIssuer 白名单支持正则——可含分隔符字符，JSON 防歧义）。</para>
/// </summary>
public static class OidcChannelConfigKeys
{
    /// <summary>平台名（google/microsoft/keycloak...——ChannelType 依据；派生平台可省略——Defaults 已含）。</summary>
    public const string Platform = "Platform";

    /// <summary>authorize 端点（直配场景可覆盖——派生平台由 Defaults 提供）。</summary>
    public const string AuthorizeUri = "AuthorizeUri";

    /// <summary>token 端点。</summary>
    public const string TokenUri = "TokenUri";

    /// <summary>userinfo 端点（可空）。</summary>
    public const string UserInfoUri = "UserInfoUri";

    /// <summary>JWKS 端点（可空——Discovery 可解析时省略）。</summary>
    public const string JwksUri = "JwksUri";

    /// <summary>Discovery 端点（配置驱动——LinkedIn 非标路径须显式）。</summary>
    public const string DiscoveryUri = "DiscoveryUri";

    /// <summary>请求 scope 列表（JSON 数组——"openid email profile"）。</summary>
    public const string Scopes = "Scopes";

    /// <summary>启用 PKCE S256（"true"/"false"；缺省 true——OidcPlatformConfig 默认值）。</summary>
    public const string UsePkce = "UsePkce";

    /// <summary>TokenIssuer 白名单（JSON 数组——支持通配/正则模式）。</summary>
    public const string TokenIssuers = "TokenIssuers";

    /// <summary>private_key_jwt 签名密钥路径（企业证书认证，可空）。</summary>
    public const string ClientAssertionSigningKeyPath = "ClientAssertionSigningKeyPath";

    /// <summary>userinfo 是否默认拉取（"true"/"false"；缺省 false）。</summary>
    public const string FetchUserInfo = "FetchUserInfo";

    /// <summary>tenant 受众（Microsoft 专属——common/consumers/organizations/{id} 端点模板；其他派生平台不写）。</summary>
    public const string Tenant = "Tenant";
}
