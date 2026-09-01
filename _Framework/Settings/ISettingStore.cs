using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置存储抽象——定义设置的 CRUD 操作（按 Provider 定位）。
    /// <para>V0.1.0 FreeSql 默认实现；后续可扩展 EF Core / 文件等。</para>
    /// </summary>
    public interface ISettingStore
    {
        /// <summary>按名称 + 提供者读取单条设置。</summary>
        Task<SettingEntity?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default);

        /// <summary>按提供者读取设置列表。</summary>
        Task<IReadOnlyList<SettingEntity>> GetListAsync(string providerName, string? providerKey, CancellationToken ct = default);

        /// <summary>写入（Upsert）设置值。</summary>
        Task SetAsync(string name, string? value, string providerName, string? providerKey, string? description, CancellationToken ct = default);

        /// <summary>删除设置。</summary>
        Task DeleteAsync(string name, string providerName, string? providerKey, CancellationToken ct = default);
    }
}
