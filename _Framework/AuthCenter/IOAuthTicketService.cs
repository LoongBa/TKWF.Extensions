using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.AuthCenter;

/// <summary>票据签发请求（方案 §5.7）。</summary>
/// <param name="AppId">目标应用 id（换取校验——防跨应用抢先消费）。</param>
/// <param name="RedirectUri">重定向 URI（白名单校验）。</param>
/// <param name="State">CSRF state（登录 vs 绑定隔离）。</param>
/// <param name="CodeVerifier">PKCE code_verifier 明文（仅存 SHA256 hash；服务端/trust 签发传 null）。</param>
/// <param name="TicketType">票据类型（OAuthTicketTypes 常量：login/bind）。</param>
/// <param name="UserId">签发即绑定用户（V0.8.0 B5——authorize 时用户已登录带 UId；末位默认参零破坏既有调用）。</param>
public sealed record OAuthTicketIssueRequest(
    string AppId,
    string RedirectUri,
    string? State = null,
    string? CodeVerifier = null,
    string TicketType = "login",
    string? UserId = null);

/// <summary>票据换取请求（方案 §5.7）。</summary>
/// <param name="Ticket">一次性票据（URL 回调承载）。</param>
/// <param name="CodeVerifier">原始 code_verifier（PKCE 校验——签发时非 null 则必填比对）。</param>
/// <param name="AppId">目标应用 id。</param>
/// <param name="State">CSRF state（与签发时比对）。</param>
public sealed record OAuthTicketExchangeRequest(string Ticket, string? CodeVerifier, string AppId, string? State);

/// <summary>票据换取结果（方案 §5.7——校验通过后签发 JWT 对）。</summary>
public sealed record OAuthTicketExchangeResult(string AccessToken, string RefreshToken, int ExpiresIn);

/// <summary>票据类型常量（方案 §5.3——登录 vs 绑定隔离，Oracle I6 防绑定劫持）。</summary>
public static class OAuthTicketTypes
{
    /// <summary>登录票据。</summary>
    public const string Login = "login";

    /// <summary>绑定票据。</summary>
    public const string Bind = "bind";
}

/// <summary>票据换取错误码（方案 §5.7 对齐需求 §5.4 Oracle N2）。</summary>
public static class OAuthTicketErrorCodes
{
    /// <summary>票据过期（400）。</summary>
    public const string TicketExpired = "TICKET_EXPIRED";

    /// <summary>票据已消费（400，单次消费防重放）。</summary>
    public const string TicketConsumed = "TICKET_CONSUMED";

    /// <summary>票据已绑定（400——CAS 条件更新败者：并发双绑 / 重复补绑）。</summary>
    public const string TicketAlreadyBound = "TICKET_ALREADY_BOUND";

    /// <summary>state/verifier/app_id 不匹配（400）。</summary>
    public const string TicketStateMismatch = "TICKET_STATE_MISMATCH";
}

/// <summary>
/// 一次性票据服务契约——签发/消费（TTL 5min 单次 + PKCE + app_id/redirect_uri 白名单 + state 防重放）。
/// <para>方案 §5.7——回调承载铁律（用户裁定 + Oracle B1）：URL 只带一次性票据 + redirect_uri，绝不带敏感信息；</para>
/// <para>纯前端静态站走公网 /oauth/exchange + PKCE code_verifier；白名单经 <c>AuthCenterOptions.RedirectUriWhitelist</c>。</para>
/// </summary>
public interface IOAuthTicketService : IDomainService
{
    /// <summary>签发一次性票据——高熵 ticket + TTL 5min + state +（可选）PKCE code_verifier hash 落库。</summary>
    /// <remarks>匿名面声明：票据签发为无会话入口（EduPlatform 转达 E1）。</remarks>
    [AllowAnonymousFlag]
    Task<string> IssueAsync(OAuthTicketIssueRequest request, CancellationToken ct = default);

    /// <summary>换取——校验 TTL/单次/state/verifier/app_id/redirect_uri → 签发 JWT 对；失败抛 AuthenticationException（错误码见 OAuthTicketErrorCodes）。</summary>
    /// <remarks>匿名面声明：票换令牌为无会话入口（EduPlatform 转达 E1）。</remarks>
    [AllowAnonymousFlag]
    Task<OAuthTicketExchangeResult> ExchangeAsync(OAuthTicketExchangeRequest request, CancellationToken ct = default);

    /// <summary>
    /// 登录后补绑——第三方回调先授权后登录绑定（authorize 签发 → 登录 → 绑定 → 换取）。
    /// <para>⚠️ V0.8.0（Oracle P0-1 安全模型）：<b>已认证帧</b>调用——userId 取 <see cref="DomainServiceBase.User"/>
    /// （当前认证用户），<b>不接受请求体 userId 参数</b>——防票据泄漏后攻击者绑定任意 userId 实现账号接管。
    /// 失败抛 AuthenticationException（TICKET_NOT_FOUND / TICKET_CONSUMED / TICKET_EXPIRED / TICKET_ALREADY_BOUND）。</para>
    /// </summary>
    Task BindTicketAsync(string ticket, CancellationToken ct = default);
}
