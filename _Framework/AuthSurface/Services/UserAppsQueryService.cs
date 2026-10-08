using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 我的应用查询门面——授权面自有（UserCenter 退役后应用查询唯一通道）。
/// <para>仅本人：userId 显式参数（装配层强制）；DTO 归本扩展主包（<see cref="UserAppDto"/>——
/// <c>UsageSummary</c> 恒 null——v0.1.0 无使用情况，儿童数据红线"不含学习明细"天然满足，v0.2.0 使用聚合）。</para>
/// </summary>
public interface IUserAppsQueryService : IDomainService
{
    /// <summary>取用户当前有效授权应用列表（Status=Active 且 ValidUntil 未过，按授权时间倒序）。</summary>
    Task<IReadOnlyList<UserAppDto>> GetAppsAsync(string userId, CancellationToken ct = default);
}

/// <summary>我的应用查询门面实现——委托 <see cref="UserAppsViewDataService"/>（跨扩展 VEntity 只读 DataService，红线合规）。</summary>
[DiContractIgnore]
internal sealed class UserAppsQueryService : DomainServiceBase, IUserAppsQueryService
{
    private readonly ILogger<UserAppsQueryService> _logger;

    private UserAppsViewDataService? _appsDataService;
    private UserAppsViewDataService AppsDataService => _appsDataService ??= User.Use<UserAppsViewDataService>();

    public UserAppsQueryService(IDomainUser user, ILogger<UserAppsQueryService> logger) : base(user)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserAppDto>> GetAppsAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var rows = await AppsDataService.GetActiveByUserIdAsync(userId, ct);
        return rows.Select(r => new UserAppDto(
                AppId: r.AppId,
                AppName: r.AppName,               // LEFT JOIN AuthApp——目录未录入 null（显示裸 AppId）
                IsAuthorized: true,               // GetActiveByUserIdAsync 已过滤 Status=Active 且 ValidUntil 未过
                ExpiresAtUtc: r.ValidUntil,
                UsageSummary: null))              // v0.1.0 无使用情况（儿童数据红线）
            .ToList();
    }
}
