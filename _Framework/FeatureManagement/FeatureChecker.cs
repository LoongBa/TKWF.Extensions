using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Core.Features;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// Feature 检查器——实现框架 <see cref="TKW.Framework.Core.Features.IFeatureChecker"/>（V4.9.50 ADR19 P4 基座）。
/// <para>解析 ambient 用户（<c>DomainUserContext.CurrentAopUser as DomainUser&lt;TUserInfo&gt;</c>，对齐 PermissionChecker）
/// → 将 <see cref="IDomainUser"/> 视图传给 <see cref="IFeatureManager"/>（不降级 IUserInfo——租户/认证信息在 IDomainUser，C2 评审裁定）
/// → 分层解析 → fail-closed（未定义 false）。</para>
/// <para>V4.10.53（领域自治根治，正确路线）：**接线型**（skill §4.2/§4.8 #7）——<see cref="IFeatureChecker"/>
/// 是主框架 Core 契约（非 <see cref="IDomainService"/>，不可修改），框架 <see cref="TKW.Framework.Domain.Interception.Filters.FeatureFilterAttribute{TUserInfo}"/>
/// 经 <c>context.ServiceProvider.GetService&lt;IFeatureChecker&gt;()</c> 普通 DI 解析（L41）——<c>AddConstructibleService</c>
/// 编译约束（where TInterface : IDomainService）不满足。旧 ctor(<see cref="IDomainUser"/>) 在普通 DI 构造时
/// IDomainUser 无可解析（永不注册 DI——D01）生产必失败（真实故障：特性检查静默失效）；改 ctor(<see cref="IServiceProvider"/>, ILogger)
/// ——<see cref="IFeatureManager"/> 经 C1 延迟解析（<c>GetRequiredService</c>，消费链内注册）。注册保持
/// <c>TryAddScoped&lt;IFeatureChecker, FeatureChecker&lt;TUserInfo&gt;&gt;()</c> 普通 DI 不变（接线型匹配）。</para>
/// <para>消费方 <c>[RequireFeature]</c> + 框架 <c>FeatureFilterAttribute</c> 经 DI 解析本实现，零改动接入。</para>
/// </summary>
public sealed class FeatureChecker<TUserInfo> : IFeatureChecker
    where TUserInfo : class, IUserInfo, new()
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FeatureChecker<TUserInfo>> _logger;
    private IFeatureManager? _manager;

    // V4.10.53（接线型）：IFeatureManager 经 C1 延迟解析（GetRequiredService——普通 DI 解析链，消费方 Use<T>() AOP 窗口内经守卫工厂）
    private IFeatureManager Manager => _manager ??= _serviceProvider.GetRequiredService<IFeatureManager>();

    public FeatureChecker(IServiceProvider serviceProvider, ILogger<FeatureChecker<TUserInfo>> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(string featureName, CancellationToken ct = default)
    {
        // ambient 用户解析（对齐 PermissionChecker——经 DomainUserContext 取当前 AOP 用户；
        // cast IDomainUser 而非 DomainUser<TUserInfo>——兼容任意 IDomainUser 实现，测试桩亦可注入）
        IDomainUser? ambient = DomainUserContext.CurrentAopUser as IDomainUser;
        return await Manager.IsEnabledAsync(featureName, ambient, ct);
    }
}
