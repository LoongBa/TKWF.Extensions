using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>SSO 档案 DTO（scope 裁剪后输出）。</summary>
/// <param name="UId">平台内部 id。</param>
/// <param name="Nickname">昵称（scope profile:basic）。</param>
/// <param name="AvatarUrl">头像 URL（scope profile:basic）。</param>
/// <param name="AuthLevel">认证强度。</param>
public sealed record SsoProfileDto(string UId, string? Nickname, string? AvatarUrl, int AuthLevel);

/// <summary>profile API 访问审计项。</summary>
/// <param name="AppId">请求应用。</param>
/// <param name="UId">目标用户。</param>
/// <param name="Scopes">请求范围。</param>
/// <param name="IpAddress">来源 IP。</param>
public sealed record SsoProfileAuditEntry(string AppId, string UId, string Scopes, string? IpAddress);

/// <summary>
/// SSO profile API 服务（server-to-server——scope 强制 + 审计）。
/// <para>设计文档 §5.7：只返回 scope 允许字段（profile:basic → nickname/avatar 子集）；
/// <b>永不返回</b> openid/channel_id/phone；经 Abstractions 契约 <c>ISsoAccountQueryService</c> 读
/// <c>SsoAccountDto</c>（敏感字段不出契约包，Oracle P1-2）；独立于用户中心聚合层（已退役）。</para>
/// </summary>
public interface ISsoProfileService : IDomainService
{
    /// <summary>取 scope 裁剪档案（未授权 scope / 账号不存在 → null；审计记录）。</summary>
    Task<SsoProfileDto?> GetProfileAsync(string appId, string uid, IReadOnlyList<string> scopes, string? ipAddress, CancellationToken ct = default);
}
