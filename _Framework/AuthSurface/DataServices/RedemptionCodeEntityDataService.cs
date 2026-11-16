using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthSurface.DTOs;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 数据服务：兑换码实体——兑换链路（生成/校验/CAS 原子兑换/惰性过期）的持久化原子方法。
/// <para>提示：标准 CRUD 逻辑和构造函数已由 RedemptionCodeEntityDataService.g.cs 承载，本分部仅编写业务原子方法。</para>
/// </summary>
partial class RedemptionCodeEntityDataService(IDomainUser user, IEntityDAC<RedemptionCodeEntity> dac)
        : DomainDataServiceBase<RedemptionCodeEntity, RedemptionCodeEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>按 CodeHash 查询（唯一索引 UX_RedemptionCode_CodeHash——兑换校验判定前置）。</summary>
    public async Task<RedemptionCodeEntity?> GetByCodeHashAsync(string codeHash, CancellationToken ct = default)
        => await EntityGetAsync(e => e.CodeHash == codeHash, ct);

    /// <summary>
    /// CAS 原子兑换——单语句 WHERE 谓词（ADR89：防 read-modify-write 竞态）：
    /// <c>Id==id AND Status==0(Available) AND (ExpireAtUtc IS NULL OR ExpireAtUtc&gt;UtcNow)</c> → 翻转 Redeemed。
    /// <para>影响 0 行 = 并发双兑败者（重查判定已兑/过期）；谓词含过期判定 = 过期码兑换天然拒绝（免先查）。</para>
    /// </summary>
    public async Task<bool> RedeemCodeAsync(long id, string redeemedByUId, DateTime redeemedAtUtc, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            where: e => e.Id == id && e.Status == 0
                && (e.ExpireAtUtc == null || e.ExpireAtUtc > DateTime.UtcNow),
            setColumns: e => new RedemptionCodeEntity
            {
                Status = 1,
                RedeemedByUId = redeemedByUId,
                RedeemedAtUtc = redeemedAtUtc,
                UpdateTime = DateTime.UtcNow,
            },
            ct: ct) > 0;

    /// <summary>惰性过期翻转——查询发现过期顺手标记 Status=2（Expired）。</summary>
    public async Task<bool> MarkExpiredAsync(long id, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            where: e => e.Id == id && e.Status == 0,
            setColumns: e => new RedemptionCodeEntity { Status = 2, UpdateTime = DateTime.UtcNow },
            ct: ct) > 0;

    /// <summary>
    /// 整批吊销（v0.3.0 T-3 码行属主）——单语句条件更新：<c>BatchId==batchId AND Status==0(Available)</c> → 翻 <c>Status=3(Revoked)</c>。
    /// <para>已兑(1)/已吊销(3)天然不命中（幂等）；返回影响行数（门面整批吊销无单码翻失败概念——批次状态机同事务落 REVOKED）。</para>
    /// </summary>
    public async Task<int> RevokeByBatchAsync(long batchId, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            where: e => e.BatchId == batchId && e.Status == 0,
            setColumns: e => new RedemptionCodeEntity { Status = 3, UpdateTime = DateTime.UtcNow },
            ct: ct);

    /// <summary>单码吊销（v0.3.0 T-3）——单语句条件更新：<c>Id==codeId AND Status==0</c> → <c>Status=3</c>；影响 0 行 = 已兑/已吊销/不存在 → 幂等 false（不抛）。</summary>
    public async Task<bool> RevokeSingleAsync(long codeId, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            where: e => e.Id == codeId && e.Status == 0,
            setColumns: e => new RedemptionCodeEntity { Status = 3, UpdateTime = DateTime.UtcNow },
            ct: ct) > 0;

    /// <summary>
    /// 该批「未兑」（status==0）码计数（v0.3.0 T-3 全吊销判定）——SQL COUNT 下推
    /// （v0.3.1：替代 V0.3.0 `EntitySelectAsync(limit:int.MaxValue)` 全量拉取 + C# Count）。
    /// </summary>
    public async Task<int> CountActiveByBatchAsync(long batchId, CancellationToken ct = default)
        => (int)await CountAsync(e => e.BatchId == batchId && e.Status == 0, ct);

    /// <summary>
    /// 该批统计分桶（v0.3.0 T-2 列表/详情统计口径）——拉取该批全部码行（status + expire_at_utc 两投影列），C# 分桶
    /// （R2 降级方案：批次码行量级 ≤5000/批、页 ≤100 批——"单查询分组优先，降级 C# 分桶"，统计正确性优先）：
    /// redeemed=Status==1；revoked=Status==3；expired=Status==0 且 expire_at_utc≤now（时间派生，禁按 Status==2 计数）；
    /// pending=Status==0 且 (null∥&gt;now)；total=GeneratedCount（头列，非本分桶）。
    /// </summary>
    public async Task<CodeBatchStatsDto> GetStatsByBatchIdAsync(long batchId, CancellationToken ct = default)
    {
        var rows = await EntitySelectAsync(e => e.BatchId == batchId, skip: 0, limit: int.MaxValue, ct: ct);
        return Bucketize(rows, DateTime.UtcNow);
    }

    /// <summary>
    /// 批量统计分桶（v0.3.1 性能整改——替代列表页 N+1 循环 <see cref="GetStatsByBatchIdAsync"/>）：
    /// **单查询** <c>GroupByAsync</c> 按 (BatchId, Status, 时间派生过期标志) 复合键 SQL GROUP BY 全下推
    /// （键含条件表达式——Bool 派生列；验证：SQLite 实测通过，PG 同族翻译由 FreeSql 方言层处理），
    /// 每批每状态一行（≤5 状态 × 页批次），C# 侧按 now 聚合分桶。
    /// <para>返回字典键=批次 Id；未命中批次（如 DRAFT 无码）→ 字典无键，调用方按零分桶兜底。</para>
    /// </summary>
    public async Task<Dictionary<long, CodeBatchStatsDto>> GetStatsByBatchIdsAsync(
        IReadOnlyCollection<long> batchIds, CancellationToken ct = default)
    {
        var result = new Dictionary<long, CodeBatchStatsDto>(batchIds.Count);
        if (batchIds.Count == 0) return result;

        var now = DateTime.UtcNow;
        var nullableIds = batchIds.Cast<long?>().ToArray();   // BatchId 为 long?（软引用列）——表达式外 Cast，IN 翻译类型匹配
        var rows = await Query
            .Where(e => nullableIds.Contains(e.BatchId))
            .GroupByAsync(
                e => new
                {
                    e.BatchId,
                    e.Status,
                    IsExpired = e.Status == 0 && e.ExpireAtUtc != null && e.ExpireAtUtc <= now,
                },
                g => g.Key.BatchId,                 // 排序须选键标量成员（匿名整键不可翻译——YearBucket 同形态）
                descending: false,
                topN: null,
                g => new
                {
                    BatchId = g.Key.BatchId,
                    Status = g.Key.Status,
                    IsExpired = g.Key.IsExpired,
                    Count = g.Count(),
                },
                ct);

        foreach (var row in rows)
        {
            var id = row.BatchId ?? 0;                  // IN 过滤后 null 不可达——纯类型兜底
            if (!result.TryGetValue(id, out var stats))
                result[id] = stats = new CodeBatchStatsDto(0, 0, 0, 0);
            long count = row.Count;
            if (row.Status == 3) stats = stats with { RevokedCount = (int)(stats.RevokedCount + count) };
            else if (row.Status == 2 || row.IsExpired) stats = stats with { ExpiredCount = (int)(stats.ExpiredCount + count) };
            else if (row.Status == 1) stats = stats with { RedeemedCount = (int)(stats.RedeemedCount + count) };
            else stats = stats with { PendingCount = (int)(stats.PendingCount + count) };
            result[id] = stats;
        }
        return result;
    }

    /// <summary>码行 C# 分桶（共享口径：red/exp/rev/pend——见 <see cref="GetStatsByBatchIdAsync"/>）。</summary>
    private static CodeBatchStatsDto Bucketize(List<RedemptionCodeEntity> rows, DateTime now)
    {
        int redeemed = 0, expired = 0, revoked = 0;
        foreach (var r in rows)
        {
            switch (r.Status)
            {
                case 1: redeemed++; break;
                case 2: expired++; break;      // 已翻转过期态（MarkExpiredAsync）——与时间派生过期同桶（不变式 total=Σ 恒成立）
                case 3: revoked++; break;
                case 0 when r.ExpireAtUtc is { } exp && exp <= now: expired++; break;
            }
        }
        return new CodeBatchStatsDto(
            RedeemedCount: redeemed,
            ExpiredCount: expired,
            RevokedCount: revoked,
            PendingCount: rows.Count - redeemed - expired - revoked);
    }
}
