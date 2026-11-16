using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
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
    /// 该批「未兑」（status==0）码计数（v0.3.0 T-3 全吊销判定）——EntitySelectAsync 拉取后 C# 计数
    /// （<c>GroupCountAsync</c> 按 BatchId 分组聚合的统计归 <c>IRedemptionQueryService/ICodeBatchService</c> 列表路径，
    /// 本方法仅吊销联动的"==0"终态判定——limit 1 拉取样即可判空，批次量级 ≤5000 全量也可接受）。
    /// </summary>
    public async Task<int> CountActiveByBatchAsync(long batchId, CancellationToken ct = default)
    {
        var rows = await EntitySelectAsync(e => e.BatchId == batchId && e.Status == 0, skip: 0, limit: int.MaxValue, ct: ct);
        return rows.Count;
    }

    /// <summary>
    /// 该批统计分桶（v0.3.0 T-2 列表/详情统计口径）——拉取该批全部码行（status + expire_at_utc 两投影列），C# 分桶
    /// （R2 降级方案：批次码行量级 ≤5000/批、页 ≤100 批——"单查询分组优先，降级 C# 分桶"，统计正确性优先）：
    /// redeemed=Status==1；revoked=Status==3；expired=Status==0 且 expire_at_utc≤now（时间派生，禁按 Status==2 计数）；
    /// pending=Status==0 且 (null∥&gt;now)；total=GeneratedCount（头列，非本分桶）。
    /// </summary>
    public async Task<CodeBatchStatsDto> GetStatsByBatchIdAsync(long batchId, CancellationToken ct = default)
    {
        var rows = await EntitySelectAsync(e => e.BatchId == batchId, skip: 0, limit: int.MaxValue, ct: ct);
        var now = DateTime.UtcNow;
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
