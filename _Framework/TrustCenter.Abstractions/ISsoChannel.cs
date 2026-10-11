using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.TrustCenter;

/// <summary>channel 认证上下文——可扩展键值模型（区别于既有 ProviderAuthenticateContext 扁平 record 缺陷）。</summary>
public sealed record SsoChannelAuthContext(IReadOnlyDictionary<string, string?> Parameters);

/// <summary>channel 认证结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="ExternalUserId">渠道身份标识（微信 = openid；成功时）。</param>
/// <param name="FailReason">失败原因（机器可读）。</param>
/// <param name="AuthLevel">认证强度（int——1=手机号 / 2=微信便捷 / 3=教师核实，对齐 Abstractions SsoAccountDto；不依赖主包 AuthLevel 枚举）。</param>
public sealed record SsoChannelAuthResult(bool Success, string? ExternalUserId, string? FailReason, int AuthLevel);

/// <summary>授权 URL 构造输入（子应用消费方接入 2026-10-11——authorize/start 端点用，ADR-Federation-子应用委托授权URL构造契约化）。</summary>
/// <param name="RedirectUri">授权回调地址（Platform Federation 回调端点——8 平台库零 redirect 配置字段，必须入参）。</param>
/// <param name="State">外部 IdP state 参数（可空 = 引擎/通道自行生成，生成值经 <see cref="SsoChannelAuthorizeResult.State"/> 回传——OIDC 引擎自生成语义）。</param>
/// <param name="Scope">OAuth scope（可空 = 通道默认，如 snsapi_base/get_user_info/auth_base）。</param>
/// <param name="Parameters">可扩展键值参数（WeCom 双流 flow/login_type 选择器等——探索约束 3）。</param>
public sealed record SsoChannelAuthorizeContext(string RedirectUri, string? State, string? Scope, IReadOnlyDictionary<string, string?> Parameters);

/// <summary>授权 URL 构造输出（字段形态对齐引擎 <c>OAuthAuthorizeResult</c>——Url/State/PkceVerifier/PkceChallenge 零转译）。</summary>
/// <param name="Url">授权 URL。</param>
/// <param name="State">实际使用的 state（入参 State 为 null 时引擎/通道生成值回传——回调侧验签衔接）。</param>
/// <param name="PkceVerifier">PKCE verifier（仅 OIDC 系启用；回调侧须能以 <c>Parameters["code_verifier"]</c> 键衔接——探索约束 2/6）。</param>
/// <param name="PkceChallenge">PKCE S256 challenge。</param>
public sealed record SsoChannelAuthorizeResult(string Url, string? State, string? PkceVerifier, string? PkceChallenge);

/// <summary>
/// SSO channel 契约（IdP 适配器接口——供平台适配扩展 <c>TKWF.Federation.{平台}</c> 实现）。
/// <para>设计文档 §4.1 channel 抽象：channel_type 决定"身份获取流程"（OAuth 代理 / Issue 票据 / 未来其他）；
/// 与既有 <c>IAuthenticationProvider</c> 的边界（Oracle P2-3）：Provider = 认证中心内部认证方式
/// （服务 token1 签发）；本契约 = 联邦 IdP 适配（外部身份源，服务 token2 联邦流）——两套并存。</para>
/// <para>注册：<c>TryAddEnumerableConstructible</c>（集合版守卫工厂，对齐 ADR92）；平台适配扩展 Initializer
/// 追加实现，未注册通道自然跳过。channel 的 IdP 凭证（如微信 AppId/AppSecret）归平台适配扩展自持（Oracle P2-4）。</para>
/// <para><b>契约归属（TrustCenter 剥离 2026-10-09）</b>：本契约随信任内核迁入 TrustCenter.Abstractions——
/// 定义"外部如何接入信任网络"，TrustCenter 经契约感知外部存在，不感知具体平台实现；Federation 编排面
/// （ISsoChannelFactory/SsoLogin）与平台库均经本契约消费。</para>
/// </summary>
public interface ISsoChannel : IDomainService
{
    /// <summary>channel 类型（wechat_oauth / wechat_event / google_oidc / ...）。</summary>
    string ChannelType { get; }

    /// <summary>channel 实例 id（微信 = 公众号 id）。</summary>
    string ChannelId { get; }

    /// <summary>身份获取流程（OAuth 代理 / Issue 票据 / 未来其他）——返回渠道身份标识。</summary>
    Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default);

    /// <summary>
    /// 构造授权 URL（子应用消费方接入 2026-10-11——Federation <c>authorize/start</c> 端点调用；ADR-Federation-子应用委托授权URL构造契约化）。
    /// <para><b>默认 null = 通道不支持</b>（端点返回 <c>AUTHORIZE_NOT_SUPPORTED</c>）——既有/未来平台库零强制；模板实例
    /// （ctor <c>ChannelConfig?</c> 为 null 的集合模板）同样返回 null（不可认证语义对齐）。</para>
    /// <para>凭证自持（Oracle P2-4）：通道 ctor <c>ChannelConfig</c> 已含 AppId/AppSecret/Extra——构造 URL 不额外索取凭证。</para>
    /// </summary>
    Task<SsoChannelAuthorizeResult?> BuildAuthorizeUrlAsync(SsoChannelAuthorizeContext context, CancellationToken ct = default) => Task.FromResult<SsoChannelAuthorizeResult?>(null);
}
