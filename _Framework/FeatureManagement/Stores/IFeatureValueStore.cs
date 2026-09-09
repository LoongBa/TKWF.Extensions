using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// Feature 值存储抽象（internal）——分层值 CRUD，经 SG1 DataService 委托。
/// <para>异常策略：读路径静默（fail-closed 降级默认值，对齐 Settings）；写路径异常传播（管理改值失败必须告知）。</para>
/// </summary>
internal interface IFeatureValueStore
{
    /// <summary>按 Name+Provider 读值（不存在返回 null；读异常 → null 静默降级）。</summary>
    Task<FeatureValueEntity?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default);

    /// <summary>管理查询（Provider 层/键可空 = 全量；读异常 → 空列表静默降级）。</summary>
    Task<IReadOnlyList<FeatureValueEntity>> GetListAsync(string? providerName, string? providerKey, CancellationToken ct = default);

    /// <summary>新增/更新值（写异常自然传播）。</summary>
    Task SetAsync(FeatureValueEntity entity, CancellationToken ct = default);

    /// <summary>按 Name+Provider 删除（写异常自然传播；不存在静默成功）。</summary>
    Task DeleteAsync(string name, string providerName, string? providerKey, CancellationToken ct = default);
}
