using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V0.6.0：默认角色提供者——从 <see cref="IUserInfo.Roles"/> 解析用户角色列表。
    /// <para>消费方可通过 DI 注册自定义 <see cref="IRoleProvider{TUserInfo}"/> 覆盖此默认实现
    /// （TryAddScoped 语义：消费方优先）。</para>
    /// </summary>
    internal sealed class DefaultRoleProvider<TUserInfo> : IRoleProvider<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        public Task<IReadOnlyList<string>> GetRolesAsync(TUserInfo userInfo)
        {
            var roles = userInfo.Roles?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList()
                        ?? new List<string>();
            return Task.FromResult<IReadOnlyList<string>>(roles);
        }
    }
}
