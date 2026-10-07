using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthSurface.DTOs;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 只读数据服务：我的应用读模型（跨扩展 VEntity）——经 <c>IEntityReadOnlyDAC</c>（框架只读路径，红线合规）。
/// <para>ADR61 自动注册；有效过滤（Status=Active 且 ValidUntil 未过）在此——C# 侧 <c>DateTime.UtcNow</c> 求值
/// 无 CURRENT_TIMESTAMP 方言风险（视图保留全量，Oracle4 P2-4：LINQ 翻译为 SQL WHERE 子句，测试断言）。</para>
/// </summary>
partial class UserAppsViewDataService(IDomainUser user, IEntityReadOnlyDAC<UserAppsView> dac)
        : DomainReadOnlyDataServiceBase<UserAppsView, UserAppsViewDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>取用户当前有效授权（Status=Active 且 ValidUntil 未过，按授权时间倒序）——仅本人。</summary>
    public async Task<List<UserAppsView>> GetActiveByUserIdAsync(string userId, CancellationToken ct = default)
        => await SelectAsync(v => v,
            predicate: v => v.UserId == userId && v.Status == 0
                && (v.ValidUntil == null || v.ValidUntil > DateTime.UtcNow),
            orderBy: q => q.OrderByDescending(v => v.CreateTime),
            limit: 200, ct: ct);
}
