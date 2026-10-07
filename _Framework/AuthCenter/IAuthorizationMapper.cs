using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 业务角色映射契约——sub + 认证声明 → 本地业务角色（Oracle I4：业务角色不进令牌，认证中心不感知角色模型）。
/// <para>方案 §5.8——消费方实现（TryAdd 语义扩展默认不注册，未实现时路径 B/A 登录装配 fail-fast）；</para>
/// <para>⚠️ V0.9.0（A.4）：<c>teacher_verified</c> claim 已移除（令牌不再携带业务声明）——角色映射经通用
/// <c>TokenValidationResult.Claims</c> 消费方业务键（教师核实走教育线业务扩展自建声明）。</para>
/// </summary>
/// <typeparam name="TUserInfo">消费方用户类型。</typeparam>
public interface IAuthorizationMapper<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>映射平台内部 id + 认证声明 → 本地业务角色列表（供 IUserInfo.Roles / IsInRole 消费）。</summary>
    Task<IReadOnlyList<string>> MapRolesAsync(string sub, TokenValidationResult token, CancellationToken ct = default);
}
