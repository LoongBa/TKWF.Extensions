using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>
/// 子应用消费方桥接门面（子应用消费方接入 2026-10-11——仅共享 Federation 模式核心编排）。
/// <para>职责：外部 IdP 认证委托的<see>编排面</see>——authorize/start 发起（state 票据 + 授权 URL 构造）、
/// oauth/callback 回调落地（验 state → 认证 → 签 accesscode）、trust/issue 已认证签发（direct 模式）、
/// identity/claim 子应用兑现（credential 鉴权 + RedeemAsync 原子取回）。</para>
/// <para>安全边界（Oracle 6 条件）：state = AES-GCM 票据（keyed <c>ISymmetricKeyProvider</c>
/// <c>SymmetricKeyProviderKeys.Federation</c>——域内自洽，Oracle 条件 3）；claim 统一
/// <c>AUTHENTICATION_FAILED</c>（防 app_id 枚举，Oracle 条件 4）；redirect 经
/// <c>ISsoClientQueryService.IsOriginAllowedAsync</c> 白名单（L2 契约，TKWF0022 合规）。</para>
/// <para>门面经 <c>AddConstructibleService</c> 注册（守卫工厂）——消费方 <c>User.Use&lt;ISsoSubAppBridge&gt;()</c>
/// 帧内解析；内部经 <c>User.Use&lt;契约&gt;()</c> 懒加载（DI004 零豁免）。</para>
/// </summary>
public interface ISsoSubAppBridge : IDomainService
{
    /// <summary>发起外部认证（authorize/start，federated 模式）——验 redirect 白名单 + 通道构造授权 URL（含 state 票据）。</summary>
    Task<AuthorizeStartResult> StartAsync(AuthorizeStartRequest request, CancellationToken ct = default);

    /// <summary>OAuth 回调落地（oauth/callback，federated 模式）——验 state 票据（解 app_id/redirect）→
    /// <c>ISsoLogin</c> 纯通道认证 → 签 accesscode（expectedClaimant=app_id）→ 返回跳回 URL。</summary>
    Task<OAuthCallbackResult> CompleteOAuthAsync(string channelId, IReadOnlyDictionary<string, string?> parameters, CancellationToken ct = default);

    /// <summary>Platform 登录态签发（trust/issue，direct 模式——已认证用户）——验白名单 → 签 accesscode
    /// （payload=<c>{mode:"direct", platform_uid}</c>，expectedClaimant=app_id）→ 返回跳回 URL。</summary>
    Task<TrustIssueResult> IssueForPlatformUserAsync(TrustIssueRequest request, string platformUid, CancellationToken ct = default);

    /// <summary>子应用兑现（identity/claim，两模式共用）——credential 鉴权（统一 <c>AUTHENTICATION_FAILED</c> 防枚举）
    /// → <c>RedeemAsync</c> 原子取回 payload。</summary>
    Task<IdentityClaimResult> ClaimAsync(IdentityClaimRequest request, CancellationToken ct = default);
}

// ─────────────────────────── 请求/结果 DTO（Web 层语义，子应用消费方协议） ───────────────────────────

/// <summary>authorize/start 请求。</summary>
/// <param name="AppId">子应用注册的 SsoClient app_id。</param>
/// <param name="Mode">发起模式：<c>federated</c>（外部 IdP 委托，本端点唯一支持）/ <c>direct</c>（拒绝——经 trust/issue）。</param>
/// <param name="ChannelId">外部 IdP 通道实例 id（federated 必填；null = 默认通道）。</param>
/// <param name="State">子应用会话防 CSRF 原值（原样经回调回传子应用，Platform 侧再包装为签名票据）。</param>
/// <param name="Redirect">子应用跳回地址（须 ∈ app_id origin 白名单——防开放重定向）。</param>
/// <param name="Scope">OAuth scope（可空 = 通道默认）。</param>
/// <param name="Parameters">可扩展通道参数（WeCom flow/login_type 等）。</param>
/// <param name="CallbackBaseUrl">Platform Federation 回调基址（<b>端点 handler 从 HttpContext 服务端可信填充</b>——OAuth redirect_uri
/// 落点 = <c>{CallbackBaseUrl}/{RoutePrefix}/oauth/{channelId}/callback</c>；请求体值被 handler 覆盖，防子应用伪造回调域）。</param>
public sealed record AuthorizeStartRequest(
    string AppId, string Mode, string? ChannelId, string? State, string Redirect, string? Scope,
    IReadOnlyDictionary<string, string?>? Parameters, string CallbackBaseUrl);

/// <summary>authorize/start 结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="AuthorizeUrl">授权 URL（前端跳转 IdP）。</param>
/// <param name="State">实际使用的 state（Platform 签名票据——回调验签解 app_id/redirect）。</param>
/// <param name="FailReason">失败原因（机器可读）。</param>
public sealed record AuthorizeStartResult(bool Success, string? AuthorizeUrl, string? State, string? FailReason);

/// <summary>oauth/callback 结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="RedirectUrl">302 跳回子应用 URL（含 code + sub_state）。</param>
/// <param name="SubState">子应用原 state（原样回传）。</param>
/// <param name="FailReason">失败原因（机器可读）。</param>
public sealed record OAuthCallbackResult(bool Success, string? RedirectUrl, string? SubState, string? FailReason);

/// <summary>trust/issue 请求（direct 模式——调用方须为 Platform 已认证用户）。</summary>
/// <param name="AppId">子应用注册的 SsoClient app_id。</param>
/// <param name="Redirect">子应用跳回地址（须 ∈ origin 白名单）。</param>
/// <param name="State">子应用会话原值（原样回传）。</param>
/// <param name="Scope">OAuth scope（可空）。</param>
public sealed record TrustIssueRequest(string AppId, string Redirect, string? State, string? Scope);

/// <summary>trust/issue 结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="RedirectUrl">302 跳回子应用 URL（含 code）。</param>
/// <param name="Code">accesscode（跳回 URL 内 code 参数）。</param>
/// <param name="FailReason">失败原因（机器可读）。</param>
public sealed record TrustIssueResult(bool Success, string? RedirectUrl, string? Code, string? FailReason);

/// <summary>identity/claim 请求。</summary>
/// <param name="AppId">子应用 app_id。</param>
/// <param name="ClientSecret">子应用 client secret（server-to-server 鉴权）。</param>
/// <param name="Code">accesscode（authorize/start 或 trust/issue 签发）。</param>
public sealed record IdentityClaimRequest(string AppId, string ClientSecret, string Code);

/// <summary>identity/claim 结果（payload 解密取回）。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="Mode">票据模式（<c>federated</c> / <c>direct</c>）。</param>
/// <param name="ChannelId">通道实例 id（federated）。</param>
/// <param name="ExternalUserId">外部 IdP 身份标识（federated——微信 openid/QQ openid/支付宝 user_id）。</param>
/// <param name="PlatformUid">Platform 用户 id（direct——子应用业务 uid 采用）。</param>
/// <param name="FailReason">失败原因（机器可读）。</param>
public sealed record IdentityClaimResult(bool Success, string? Mode, string? ChannelId, string? ExternalUserId, string? PlatformUid, string? FailReason);
