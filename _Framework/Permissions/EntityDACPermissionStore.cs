using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// 基于 <see cref="PermissionGrantEntityDataService"/> 的权限存储实现——数据访问红线整改
    /// （2026-09-07 用户裁定）：扩展不直接注入 IEntityDAC，须经 SG1/xCodeGen 生成的 DataService。
    /// <para>委托 <see cref="PermissionGrantEntityDataService.GetGrantAsync"/> + <see cref="PermissionGrantEntityDataService.SetGrantAsync"/>
    /// （按三列业务键查询/upsert——逻辑与原 EntityDAC 直用重复，此处收敛）。</para>
    /// <para><b>生命周期</b>：Scoped（依赖 Scoped DataService，自动参与当前请求 UoW 事务）。</para>
    /// </summary>
    public sealed class EntityDACPermissionStore : IPermissionStore
    {
        private readonly PermissionGrantEntityDataService _dataService;

        public EntityDACPermissionStore(PermissionGrantEntityDataService dataService)
        {
            _dataService = dataService;
        }

        public async Task<PermissionGrantResult> GetAsync(string permissionName, string providerName, string providerKey)
        {
            var dto = await _dataService.GetGrantAsync(permissionName, providerName, providerKey, CancellationToken.None);
            return dto?.IsGranted == true ? PermissionGrantResult.Granted : PermissionGrantResult.Denied;
        }

        public async Task SetAsync(string permissionName, string providerName, string providerKey, bool isGranted)
        {
            // upsert：DataService.SetGrantAsync 内部按三列业务键查存在 → 更新 IsGranted / 插入
            await _dataService.SetGrantAsync(permissionName, providerName, providerKey, isGranted, CancellationToken.None);
        }

        /// <summary>按 provider 批量读取已授予权限名集合（N+1 优化 V0.8.1）——委托 DataService 一次查询。</summary>
        public async Task<HashSet<string>> GetGrantedPermissionNamesAsync(
            string providerName, IEnumerable<string>? providerKeys = null)
        {
            var names = await _dataService.GetGrantedNamesByProviderAsync(
                providerName, providerKeys, CancellationToken.None);
            return names;
        }
    }
}