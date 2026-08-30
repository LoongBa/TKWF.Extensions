using System.Linq;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// 基于 <see cref="IEntityDAC{TEntity}"/> 的权限存储实现——ORM 无关，注入 IEntityDAC 即可工作。
    /// <para>扩展自带持久化实现，不依赖 FreeSql/EF Core——消费方调用 <c>UseFreeSqlEntityDAC()</c>
    /// 注册 <c>FreeSqlEntityDAC&lt;PermissionGrantEntity&gt;</c> 即可自动接线。</para>
    /// <para><b>生命周期</b>：Scoped（依赖 Scoped <c>IEntityDAC&lt;T&gt;</c> + <c>UnitOfWorkManager</c>）。
    /// 消费方 <c>IPermissionStore</c> 注册为 Scoped（与 <c>PermissionChecker&lt;TUserInfo&gt;</c> 一致），
    /// 自动参与当前请求 UoW 事务。</para>
    /// </summary>
    public sealed class EntityDACPermissionStore : IPermissionStore
    {
        private readonly IEntityDAC<PermissionGrantEntity> _dac;

        public EntityDACPermissionStore(IEntityDAC<PermissionGrantEntity> dac)
        {
            _dac = dac;
        }

        public async Task<PermissionGrantResult> GetAsync(string permissionName, string providerName, string providerKey)
        {
            var query = _dac.Query
                .Where(g => g.PermissionName == permissionName
                            && g.ProviderName == providerName
                            && g.ProviderKey == providerKey);
            var grant = await _dac.FirstOrDefaultAsync(query);
            return grant?.IsGranted == true ? PermissionGrantResult.Granted : PermissionGrantResult.Denied;
        }

        public async Task SetAsync(string permissionName, string providerName, string providerKey, bool isGranted)
        {
            // upsert：存在则更新 IsGranted，不存在则插入（按三列业务键，非主键）
            var query = _dac.Query
                .Where(g => g.PermissionName == permissionName
                            && g.ProviderName == providerName
                            && g.ProviderKey == providerKey);
            var existing = await _dac.FirstOrDefaultAsync(query);

            if (existing != null)
            {
                existing.IsGranted = isGranted;
                await _dac.UpdateAsync(existing);
            }
            else
            {
                await _dac.InsertAsync(new PermissionGrantEntity
                {
                    PermissionName = permissionName,
                    ProviderName = providerName,
                    ProviderKey = providerKey,
                    IsGranted = isGranted
                });
            }
        }
    }
}
