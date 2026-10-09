using System.Collections.Generic;

namespace TKWF.Federation.Oidc;

/// <summary>
/// OIDC 平台配置（M1 基座——国外规划 §五落地：配置驱动 channel 适配，协议差异收敛于配置）。
/// <para>端点全部可覆盖（Oracle 复核：LinkedIn Discovery 位于 <c>/oauth/.well-known/</c> 非标准根路径、
/// Apple 双端点并存——Discovery 必须配置驱动，硬编码路径必错）；凭证自持（Oracle P2-4：
/// ClientSecret/私钥归基座 Options，生产 AES-GCM 密文或装配注入，永不明文进配置库）。</para>
/// <para>sub 语义：外部登录身份键恒 = id_token sub（P12 不透明字符串整体持久化，不解析结构）；
/// pairwise 隔离经 channel_id 复合编码（<c>BuildChannelId</c>——N2 §3.3 定案不依赖本配置）。</para>
/// </summary>
public sealed class OidcPlatformConfig
{
    /// <summary>channel 实例 id（<c>ISsoChannel.ChannelId</c> 选区依据；派生平台库经 <see cref="OidcChannelBase.BuildChannelId"/> 复合编码）。</summary>
    public string ChannelId { get; set; } = "";

    /// <summary>
    /// 通道对外别名（方案 §3.7 双键——可空，缺省 null = 对外用 <see cref="ChannelId"/>，存量零迁移）。
    /// <para>语义：alias = 对外名（URL 路径段 /sso/oauth/{alias}/callback / 展示 / 客户端入参），与内部
    /// <see cref="ChannelId"/> 一一对应；可读直观 + 保护内部持久化键。解析统一经
    /// <c>IChannelRegistry.GetByAliasOrIdAsync</c>（先 alias → 再 ChannelId）。</para>
    /// </summary>
    public string? Alias { get; set; }

    /// <summary>平台名（google/microsoft/linkedin/slack/keycloak...——ChannelType 依据）。</summary>
    public string Platform { get; set; } = "";

    /// <summary>OIDC client_id（映射表 pairwise 复合编码依据——public 平台通配 "*"）。</summary>
    public string ClientId { get; set; } = "";

    /// <summary>OIDC client_secret（凭证自持；或 private_key_jwt 断言时可为空）。</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>authorize 端点（必填——硬编码端点，如 Apple 无 discovery 场景）。</summary>
    public string AuthorizeUri { get; set; } = "";

    /// <summary>token 端点（必填；Discovery 可用时亦可经 Discovery 解析覆盖）。</summary>
    public string TokenUri { get; set; } = "";

    /// <summary>userinfo 端点（可选——标准 OIDC 平台均提供）。</summary>
    public string? UserInfoUri { get; set; }

    /// <summary>JWKS 端点（可选——Discovery 可解析时省略）。</summary>
    public string? JwksUri { get; set; }

    /// <summary>
    /// Discovery 端点（可选配置驱动——LinkedIn <c>/oauth/.well-known/</c> 非标准路径须显式；Apple 双端点并存
    /// issuer 冲突场景可省略 discovery 纯端点配置）。配置时 DiscoverAsync 尝试解析 token/userinfo/jwks 覆盖。
    /// </summary>
    public string? DiscoveryUri { get; set; }

    /// <summary>请求 scope（openid 必含；平台附加 email/profile 等）。</summary>
    public List<string> Scopes { get; set; } = ["openid"];

    /// <summary>启用 PKCE S256（OAuth 2.1 对齐——Google/Microsoft/LinkedIn/Slack 均支持，默认 true）。</summary>
    public bool UsePkce { get; set; } = true;

    /// <summary>
    /// TokenIssuer 白名单（id_token `iss` 校验——支持通配/正则模式；
    /// Microsoft common/consumers/organizations 授权后 iss 含实际租户 GUID 须通配匹配——M2-P1-1）。
    /// 空 = 不校验 iss（不推荐，Apple 双端点场景可配精确值）。
    /// </summary>
    public List<string> TokenIssuers { get; set; } = [];

    /// <summary>private_key_jwt 认证（企业场景证书认证；null = 用 ClientSecret post 认证）。</summary>
    public string? ClientAssertionSigningKeyPath { get; set; }

    /// <summary>userinfo 是否默认拉取（true=AuthenticateAsync 内拉取并入 DTO；false=懒加载由调用方决定）。</summary>
    public bool FetchUserInfo { get; set; }
}