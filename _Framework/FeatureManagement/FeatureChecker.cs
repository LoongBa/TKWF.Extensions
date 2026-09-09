using System;
using System.Threading;
using System.Threading.Tasks;
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
/// <para>消费方 <c>[RequireFeature]</c> + 框架 <c>FeatureFilterAttribute</c> 经 DI 解析本实现，零改动接入。</para>
/// </summary>
public sealed class FeatureChecker<TUserInfo> : IFeatureChecker
    where TUserInfo : class, IUserInfo, new()
{
    private readonly IFeatureManager _manager;
    private readonly ILogger<FeatureChecker<TUserInfo>> _logger;

    public FeatureChecker(IFeatureManager manager, ILogger<FeatureChecker<TUserInfo>> logger)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(string featureName, CancellationToken ct = default)
    {
        // ambient 用户解析（对齐 PermissionChecker——经 DomainUserContext 取当前 AOP 用户；
        // cast IDomainUser 而非 DomainUser<TUserInfo>——兼容任意 IDomainUser 实现，测试桩亦可注入）
        IDomainUser? ambient = DomainUserContext.CurrentAopUser as IDomainUser;
        return await _manager.IsEnabledAsync(featureName, ambient, ct);
    }
}
