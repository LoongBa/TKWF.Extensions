using System.Collections.Generic;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V0.6.0：角色提供者——为权限检查器解析用户的角色列表。
    /// <para>默认实现 <see cref="DefaultRoleProvider{TUserInfo}"/> 从 <c>IUserInfo.Roles</c> 读取。
    /// 消费方可替换为自定义实现（如从角色服务、缓存或外部身份提供商解析）。</para>
    /// </summary>
    /// <typeparam name="TUserInfo">用户信息类型（扩展泛型约束）。</typeparam>
    public interface IRoleProvider<TUserInfo> where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>获取用户的角色列表（空集合 = 无角色，不做任何权限授予）。</summary>
        Task<IReadOnlyList<string>> GetRolesAsync(TUserInfo userInfo);
    }
}
