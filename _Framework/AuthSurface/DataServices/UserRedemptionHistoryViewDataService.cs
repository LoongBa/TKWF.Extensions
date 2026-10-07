using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthSurface.DTOs;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 只读数据服务：兑换历史读模型（跨扩展 VEntity）——经 <c>IEntityReadOnlyDAC</c>（框架只读路径，红线合规）。
/// <para>ADR61 自动注册（DomainReadOnlyDataServiceBase 基类判定——Initializer 零手动注册）。
/// 仅本人过滤在 predicate（<c>UserId == userId</c>）——IDOR 防护数据层兜底。</para>
/// </summary>
partial class UserRedemptionHistoryViewDataService(IDomainUser user, IEntityReadOnlyDAC<UserRedemptionHistoryView> dac)
        : DomainReadOnlyDataServiceBase<UserRedemptionHistoryView, UserRedemptionHistoryViewDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>取用户兑换历史（Redeemed 态，按兑换时间倒序）——仅本人。</summary>
    public async Task<List<UserRedemptionHistoryView>> GetHistoryByUserIdAsync(string userId, CancellationToken ct = default)
        => await SelectAsync(v => v,
            predicate: v => v.UserId == userId && v.Status == 1,
            orderBy: q => q.OrderByDescending(v => v.RedeemedAtUtc),
            limit: 200, ct: ct);
}
