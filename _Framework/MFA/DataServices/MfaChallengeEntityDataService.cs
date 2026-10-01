using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.MFA.DTOs;

namespace TKWF.Ext.MFA;

/// <summary>
/// MFA 挑战票据 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para>删除语义：<c>hasSoftDelete:false</c>——物理删除（Disable 级联清理未消费挑战）。</para>
/// <para>单次消费：<c>MarkConsumedAsync</c> 翻转 IsConsumed（防重放，Oracle C1——Disable 级联等非并发路径）；
/// <c>MarkConsumedIfActiveAsync</c> 原子条件消费（WHERE IsConsumed=false 守卫——并发验证恰一成功）——
/// <b>数据访问红线逃生口</b>（Oracle 裁决路径 B）：引擎级单语句 UPDATE 保证并发原子性，IEntityDAC 无法表达条件更新，
/// 经构造注入 IFreeSql 直执行（DataPort raw SQL 先例）；尝试频控归内存窗口（Oracle C4，无 Attempts 列）。</para>
/// <para>⚠️ <c>[DiContractIgnore]</c>（V4.10.21 ADR71）：构造参数 <see cref="IFreeSql"/> 为逃生口（非领域服务
/// 接口、框架内置白名单外）——SG1a DI001 校验豁免标注（纯警告豁免，不改注册语义；逃生口裁决记录见 README/开发方案）。</para>
/// </summary>
[TKW.Framework.CodeGeneration.DiContractIgnore]
partial class MfaChallengeEntityDataService(IDomainUser user, IEntityDAC<MfaChallengeEntity> dac, IFreeSql fsql)
    : DomainDataServiceBase<MfaChallengeEntity, MfaChallengeEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>裸 ORM 逃生口（Oracle 裁决路径 B——仅承载原子条件更新，不做常规数据访问）。</summary>
    private readonly IFreeSql _fsql = fsql;
    // ── Service 委托路径的业务方法 ──

    /// <summary>按 Id 查挑战票据（challengeId = 主键；未消费 + 未过期——VerifyChallenge 入口）。</summary>
    public Task<MfaChallengeEntity?> GetActiveByIdAsync(long id, CancellationToken ct = default)
        => EntityGetAsync(f => f.Id == id && !f.IsConsumed && f.ExpireAt > DateTime.UtcNow, ct);

    /// <summary>按用户 + 方法查最新活动挑战（重发拒绝判定 / Disable 级联清理）。</summary>
    public async Task<IReadOnlyList<MfaChallengeEntity>> GetActiveByUserMethodAsync(string userId, string method, CancellationToken ct = default)
        => await EntitySelectAsync(f => f.UserId == userId && f.Method == method && !f.IsConsumed && f.ExpireAt > DateTime.UtcNow,
            0, MaxQuerySize, q => q.OrderByDescending(f => f.CreateTime), ct);

    /// <summary>新增挑战票据（回写自增 Id——即 challengeId）。</summary>
    public Task<MfaChallengeEntity> CreateAsync(MfaChallengeEntity entity, CancellationToken ct = default)
        => EntityCreateAsync(entity, ct);

    /// <summary>单次消费：翻转 IsConsumed=true（成功验证后调用；原子性由 DB 更新保证——非并发路径保留）。</summary>
    public Task<MfaChallengeEntity> MarkConsumedAsync(long id, CancellationToken ct = default)
        => EntityUpdateAsync(new MfaChallengeEntity { Id = id, IsConsumed = true }, ct);

    /// <summary>
    /// 原子条件消费：WHERE IsConsumed=false 守卫（Oracle C1——并发验证恰一成功，防重放）。
    /// <para>返回 1 = 成功（本请求获得独占消费）；0 = 败（已被并发消费或已过期——调用方按失败处理）。</para>
    /// <para>数据访问红线逃生口（Oracle 裁决路径 B）：IEntityDAC 按主键无条件 UPDATE 无法表达竞争守卫，
    /// 引擎级单语句条件 UPDATE 天然原子（DataPort raw SQL 先例）；注入 IFreeSql 由 ActivatorUtilities 自动解析。</para>
    /// </summary>
    public async Task<int> MarkConsumedIfActiveAsync(long id, DateTime now, CancellationToken ct = default)
        => await _fsql.Update<MfaChallengeEntity>()
            .Set(x => x.IsConsumed, true)
            .Where(x => x.Id == id && !x.IsConsumed && x.ExpireAt > now)
            .ExecuteAffrowsAsync(ct);

    /// <summary>物理删除挑战（Disable 级联清理）。</summary>
    public Task DeleteAsync(long id, CancellationToken ct = default)
        => EntityDeleteBatchAsync(new[] { id }, ct);

    /// <summary>查询批量上限（挑战低频，兜底）。</summary>
    private const int MaxQuerySize = 1000;
}
