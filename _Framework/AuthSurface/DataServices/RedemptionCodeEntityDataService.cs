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
}
