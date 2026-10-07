using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：密码历史实体（V0.9.0 ADR-密码策略与口令协议 决策 5——历史防重用 + 最近改密时间承载，只增表）。
/// <para>红线（数据访问）：历史读写经 SG1 生成 <see cref="PasswordHistoryEntityDataService"/>——零 ORM 直注入。</para>
/// <para>只增语义：仅 CreateAsync 追加写（对齐 SecurityLog 只增不改先例）——历史裁剪由门面按 HistoryRetentionCount 删旧代。</para></summary>
// 提示：标准 CRUD 逻辑和构造函数已由 PasswordHistoryEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
 partial class PasswordHistoryEntityDataService(IDomainUser user, IEntityDAC<PasswordHistoryEntity> dac)
        : DomainDataServiceBase<PasswordHistoryEntity, PasswordHistoryEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }

    /// <summary>追加一条密码历史（只增——改密落地时调用；CreateTime UTC 兜底）。</summary>
    public async Task CreateAsync(PasswordHistoryEntity entity, CancellationToken ct = default)
    {
        entity.CreateTime = DateTime.UtcNow;
        await EntityCreateAsync(entity, ct);
    }

    /// <summary>取最近 N 代密码历史（CreateTime 降序——防重用比对源；UId 归属）。</summary>
    public async Task<List<PasswordHistoryEntity>> GetRecentAsync(string uid, int count, CancellationToken ct = default)
        => await EntitySelectAsync(h => h.UId == uid, 0, count,
            q => q.OrderByDescending(h => h.CreateTime), ct);

    /// <summary>最近改密时间（强制轮换判定——最新历史行 CreateTime；无历史返回 null）。</summary>
    public async Task<DateTime?> GetLastChangeTimeAsync(string uid, CancellationToken ct = default)
    {
        var recent = await GetRecentAsync(uid, 1, ct);
        return recent.Count > 0 ? recent[0].CreateTime : null;
    }
}