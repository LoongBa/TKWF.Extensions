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
/// 兑换历史查询门面——授权面自有（UserCenter 退役后兑换查询唯一通道）。
/// <para>仅本人：userId 显式参数（装配层强制）；DTO 归本扩展主包（<see cref="RedemptionRecordDto"/>）。</para>
/// </summary>
public interface IRedemptionQueryService : IDomainService
{
    /// <summary>取用户兑换历史（Redeemed 态，按时间倒序；code 已脱敏透传）。</summary>
    Task<IReadOnlyList<RedemptionRecordDto>> GetRedemptionsAsync(string userId, CancellationToken ct = default);
}

/// <summary>兑换历史查询门面实现——委托 <see cref="UserRedemptionHistoryViewDataService"/>（跨扩展 VEntity 只读 DataService，红线合规）。</summary>
[DiContractIgnore]
internal sealed class RedemptionQueryService : DomainServiceBase, IRedemptionQueryService
{
    private readonly ILogger<RedemptionQueryService> _logger;

    private UserRedemptionHistoryViewDataService? _historyDataService;
    private UserRedemptionHistoryViewDataService HistoryDataService
        => _historyDataService ??= User.Use<UserRedemptionHistoryViewDataService>();

    public RedemptionQueryService(IDomainUser user, ILogger<RedemptionQueryService> logger) : base(user)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<IReadOnlyList<RedemptionRecordDto>> GetRedemptionsAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var rows = await HistoryDataService.GetHistoryByUserIdAsync(userId, ct);
        return rows.Select(r => new RedemptionRecordDto(
                CodeMasked: r.CodeMasked,
                ProductName: r.ProductName,
                TargetAppId: r.TargetAppId,
                RedeemedAtUtc: r.RedeemedAtUtc ?? default,   // Status=1 行必有（CAS 兑换设置）；视图 JOIN 保证
                Status: "redeemed"))
            .ToList();
    }
}
