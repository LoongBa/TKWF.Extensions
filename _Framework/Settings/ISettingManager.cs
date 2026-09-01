using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Settings
{
    /// <summary>
    /// 设置管理器——分层读写（User → Tenant → Global → 默认值）。
    /// <para>消费方通过此接口进行设置的读写，无需关心底层 Provider 分层逻辑。</para>
    /// </summary>
    public interface ISettingManager
    {
        /// <summary>读取设置值（分层查找，返回字符串或默认值）。</summary>
        Task<string> GetAsync(string name, string defaultValue = "", CancellationToken ct = default);

        /// <summary>读取设置值并反序列化为指定类型。</summary>
        Task<T> GetAsync<T>(string name, T defaultValue = default!, CancellationToken ct = default);

        /// <summary>写入设置值到当前用户层。</summary>
        Task SetAsync(string name, string value, CancellationToken ct = default);

        /// <summary>写入设置值并序列化为 JSON。</summary>
        Task SetAsync<T>(string name, T value, CancellationToken ct = default);
    }
}
