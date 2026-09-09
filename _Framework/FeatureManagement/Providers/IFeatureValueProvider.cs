using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// Feature 值 Provider 扩展点（对齐 ABP IFeatureValueProvider）——自定义解析层可插入
/// <see cref="FeatureOptions.ProviderOrder"/> 有序链。内置四层（User/Role/Tenant/Global）已实现；
/// 消费方实现本接口 + DI 注册（AddScoped）即追加新层。
/// <para>用户上下文约定（评审 C1）：Provider 自行注入 <c>IDomainUser</c>（Scoped）等上下文；
/// <paramref name="providerKey"/> 由 Manager 按 Provider.Name 约定传入（内置四层已解析）——
/// 自定义 Provider 如需特殊 key 提取（Edition/Device 等），注入上下文自行处理。</para>
/// </summary>
public interface IFeatureValueProvider
{
    /// <summary>Provider 唯一标识（对应 <see cref="FeatureProviders"/> 常量；全仓库唯一，冲突懒校验）。</summary>
    string Name { get; }

    /// <summary>读取 Feature 值（未设置返回 null——回退下一 Provider）。</summary>
    Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default);
}
