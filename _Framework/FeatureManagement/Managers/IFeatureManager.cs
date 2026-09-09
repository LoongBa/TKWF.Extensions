using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// 功能管理门面——分层值解析（User→Role→Tenant→Global→默认）+ 缓存 + 管理写路径。
/// <para>用户契约（C2 评审裁定）：接收 <see cref="IDomainUser"/>（IsAuthenticated/UserId/TenantId/UserInfo.Roles 均在其上），
/// 不降级 IUserInfo——租户/认证信息在 IDomainUser。</para>
/// </summary>
public interface IFeatureManager
{
    /// <summary>分层解析 Feature 值（User→Role(遍历序首个命中)→Tenant(仅 TenantId.HasValue)→Global→defaultValue）；匿名直查 Global。</summary>
    Task<string> GetValueAsync(string name, IDomainUser? user, string? defaultValue = null, CancellationToken ct = default);

    /// <summary>布尔开关解析：命中层值存在但 bool.TryParse 失败 → false（不跨层回退，P3）；未定义 → false（fail-closed）。</summary>
    Task<bool> IsEnabledAsync(string name, IDomainUser? user, CancellationToken ct = default);

    /// <summary>设置 Feature 值（管理写路径——Global 层应用层唯一性保证 + 缓存失效；写异常传播）。</summary>
    Task SetValueAsync(string name, string value, string providerName, string providerKey, CancellationToken ct = default);

    /// <summary>删除 Feature 值（缓存失效；写异常传播）。</summary>
    Task DeleteValueAsync(string name, string providerName, string providerKey, CancellationToken ct = default);

    /// <summary>全部定义（管理/展示）。</summary>
    Task<IReadOnlyList<FeatureDefinition>> GetDefinitionsAsync(CancellationToken ct = default);

    /// <summary>Feature 值列表（管理浏览；Provider 层/键可空过滤——读路径，Store 直读静默降级）。</summary>
    Task<IReadOnlyList<FeatureValueEntity>> GetFeatureValuesAsync(
        string? providerName = null, string? providerKey = null, CancellationToken ct = default);

    /// <summary>命中层返回（管理显示——写后最新值）。</summary>
    Task<(string? Value, string ProviderName)> GetEffectiveValueAsync(string name, IDomainUser? user, CancellationToken ct = default);

    /// <summary>类型化读取（v0.3.0）——按 T 反序列化（对齐 Settings <c>GetAsync&lt;T&gt;</c>）。
    /// <para>映射：bool/int/long/decimal/double 用 TryParse（InvariantCulture，DateTime 用 RoundtripKind）；
    /// string 原样；其他（含 Json 对象/数组/record）经 <c>JsonSerializer.Deserialize</c>。
    /// <b>解析失败/无值 → defaultValue</b>（fail-closed 精神：不抛，返回默认；Json 引用类型解析失败返回 null）。</para>
    /// <para>回退链：存储值 → 定义 DefaultValue（可解析则用之）→ defaultValue。</para></summary>
    Task<T> GetValueAsync<T>(string name, IDomainUser? user, T defaultValue = default!, CancellationToken ct = default);

    /// <summary>类型化写入（v0.3.0）——value 序列化为规范字符串后走 <see cref="SetValueAsync(string, string, string, string, CancellationToken)"/>（含写时校验）。
    /// <para>映射：bool → "true"/"false"（规范小写）；int/long/decimal/double → InvariantCulture；
    /// DateTime → ISO8601（"O"）；string 原样；其他（含 Json 对象/数组/record）→ <c>JsonSerializer.Serialize</c>。</para></summary>
    Task SetValueAsync<T>(string name, T value, string providerName, string providerKey, CancellationToken ct = default);
}
