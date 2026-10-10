using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部 IdP 登录编排门面（T5 三层边界 2026-10-09——替代已删微信双源登录门面 <c>IWechatLoginService</c>）。
/// <para>AuthCenter 内网经 <see cref="IExternalIdpAuthenticator"/> 契约<b>借道 Federation 连接层</b>完成外部身份认证
/// （微信/QQ/支付宝/OIDC 等平台库通道——平台协议单源归平台网关库，AuthCenter 零双实现）。</para>
/// <para>流程（方案 §5.4）：桥接认证（<b>fail-hard</b>：Federation 未装配 → <c>User.Use&lt;IExternalIdpAuthenticator&gt;()</c>
/// 抛守卫，不静默降级）→ <see cref="ISsoChannelMapService"/> 映射（channelId 从 parameters["channel_id"]）→
/// 无映射建号（<see cref="IAuthAccountService"/>，AuthLevel.Federated）+ LinkAsync → 回读账号 →
/// <see cref="ITokenService.IssueTokenAsync"/> 签 token1（authType=<c>federated</c> + <c>channel_type</c> claim）。</para>
/// <para>表现层经 <c>User.Use&lt;IExternalIdpLoginService&gt;().LoginAsync(channelType, parameters)</c> 编排——
/// 控制器不得 <c>[FromServices] IEnumerable&lt;IAuthenticationProvider&gt;</c>（帧外抛守卫）；端点
/// <c>POST {prefix}/login/external/{{channelType}}</c>（body = 参数字典含 code/channel_id 等）。</para>
/// </summary>
public interface IExternalIdpLoginService : IDomainService
{
    /// <summary>外部 IdP 登录（桥接认证 → 映射建号/复用 → 签发令牌）——返回 <see cref="LoginResult"/>。
    /// <paramref name="parameters"/> 原样透传桥接（含 code/channel_id 等平台协议字段）。</summary>
    [AllowAnonymousFlag]
    Task<LoginResult> LoginAsync(string channelType, IReadOnlyDictionary<string, string?> parameters, CancellationToken ct = default);
}
