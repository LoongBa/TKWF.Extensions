using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 跨系统映射服务——平台内部 id ↔ 业务 app + 业务本地 id + UnionId（统一 DMP 双机制，方案 §5.9）。
/// <para>DMP V4.0 管理员走映射表、会员走外键——Ext 以映射表统一承载；DMP 迁移时会员映射迁入本表（需求 §6 约束 1）。</para>
/// <para>数据访问红线合规：不注入 IFreeSql/IEntityDAC——全部经 <see cref="PlatformAccountMapEntityDataService"/>
/// 内部转发访问器（Entity*，同程序集）委托查询/写入。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册改 <c>AddConstructibleService&lt;IPlatformAccountMapService, PlatformAccountMapService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
public sealed class PlatformAccountMapService : DomainServiceBase, IPlatformAccountMapService
{
    private PlatformAccountMapEntityDataService? _dataService;
    private readonly ILogger<PlatformAccountMapService> _logger;

    private PlatformAccountMapEntityDataService DataService => _dataService ??= User.Use<PlatformAccountMapEntityDataService>();

    public PlatformAccountMapService(IDomainUser user, ILogger<PlatformAccountMapService> logger) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<PlatformAccountMapEntity?> GetByPlatformAsync(string platformAccountId, string businessAppId, CancellationToken ct = default)
        => DataService.EntityGetAsync(m => m.PlatformAccountId == platformAccountId && m.BusinessAppId == businessAppId, ct);

    /// <inheritdoc />
    public Task<PlatformAccountMapEntity?> GetByBusinessAsync(string businessAppId, string businessLocalId, CancellationToken ct = default)
        => DataService.EntityGetAsync(m => m.BusinessAppId == businessAppId && m.BusinessLocalId == businessLocalId, ct);

    /// <inheritdoc />
    public async Task<PlatformAccountMapEntity> LinkAsync(
        string platformAccountId, string businessAppId, string businessLocalId, string? unionId, CancellationToken ct = default)
    {
        // upsert：同键（唯一索引 UX_PlatformAccountMap）存在 → 更新 UnionId（仅当提供且变化）+ UpdateTime=UtcNow；
        // 不存在 → 新建（CreateTime/UpdateTime=UtcNow，Id 由 EntityCreateAsync 回填）。
        var existing = await DataService.EntityGetAsync(
            m => m.PlatformAccountId == platformAccountId
                && m.BusinessAppId == businessAppId
                && m.BusinessLocalId == businessLocalId, ct);

        if (existing != null)
        {
            if (!string.IsNullOrEmpty(unionId) && !string.Equals(existing.UnionId, unionId, StringComparison.Ordinal))
            {
                existing.UnionId = unionId;
                existing.UpdateTime = DateTime.UtcNow;
                await DataService.EntityUpdateAsync(existing, ct);
            }
            return existing;   // 无变更 → 幂等返回，不写库
        }

        var created = new PlatformAccountMapEntity
        {
            PlatformAccountId = platformAccountId,
            BusinessAppId = businessAppId,
            BusinessLocalId = businessLocalId,
            UnionId = unionId,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        };
        return await DataService.EntityCreateAsync(created, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlatformAccountMapEntity>> GetByUnionIdAsync(string unionId, CancellationToken ct = default)
        => await DataService.EntitySelectAsync(m => m.UnionId == unionId, ct: ct);
}
