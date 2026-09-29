using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 业务角色映射契约——sub + 认证声明 → 本地业务角色（Oracle I4：业务角色不进令牌，认证中心不感知角色模型）。
/// <para>方案 §5.8——消费方实现（TryAdd 语义扩展默认不注册，未实现时路径 B/A 登录装配 fail-fast）；</para>
/// <para><c>teacher_verified</c> 仅身份声明，如需教师角色在 MapRoles 中映射。</para>
/// </summary>
/// <typeparam name="TUserInfo">消费方用户类型。</typeparam>
public interface IAuthorizationMapper<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>映射平台内部 id + 认证声明 → 本地业务角色列表（供 IUserInfo.Roles / IsInRole 消费）。</summary>
    Task<IReadOnlyList<string>> MapRolesAsync(string sub, TokenValidationResult token, CancellationToken ct = default);
}
