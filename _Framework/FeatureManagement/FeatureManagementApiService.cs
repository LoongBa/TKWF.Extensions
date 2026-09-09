using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// 功能管理 API 服务（V0.1.0 管理接口）——控制器写路径统一经 <see cref="IFeatureManager"/>（缓存失效 + Global 唯一性），
/// 禁止直写 DataService（裸写绕过缓存失效与 Global 预检——C3 评审裁定）。
/// <para>消费方 SG1b 经 <c>[GenerateController]</c> 生成 REST 端点（对齐 IdentityAuthService 先例——
/// 普通 [GenerateController] 类所有 public async 方法自动纳入控制器）。</para>
/// </summary>
[GenerateController]
public partial class FeatureManagementApiService : DomainServiceBase
{
    private readonly IFeatureManager _manager;

    public FeatureManagementApiService(IDomainUser user, IFeatureManager manager)
        : base(user)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <summary>设置 Feature 值（管理写路径——经 Manager：Global 唯一性 + 缓存失效 + 写异常传播）。</summary>
    public Task SetFeatureValueAsync(string name, string value, string providerName, string providerKey,
        CancellationToken ct = default)
        => _manager.SetValueAsync(name, value, providerName, providerKey, ct);

    /// <summary>删除 Feature 值（经 Manager：缓存失效）。</summary>
    public Task DeleteFeatureValueAsync(string name, string providerName, string providerKey,
        CancellationToken ct = default)
        => _manager.DeleteValueAsync(name, providerName, providerKey, ct);

    /// <summary>查询 Feature 值列表（管理浏览；按 Provider 层/键可空过滤——读路径，Manager 门面静默降级）。</summary>
    public Task<IReadOnlyList<FeatureValueEntity>> GetFeatureValuesAsync(
        string? providerName = null, string? providerKey = null, CancellationToken ct = default)
        => _manager.GetFeatureValuesAsync(providerName, providerKey, ct);

    /// <summary>全部 Feature 定义（管理展示）。</summary>
    public Task<IReadOnlyList<FeatureDefinition>> GetFeatureDefinitionsAsync(CancellationToken ct = default)
        => _manager.GetDefinitionsAsync(ct);
}
