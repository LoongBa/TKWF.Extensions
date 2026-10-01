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
/// MFA 恢复码 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para>删除语义：<c>hasSoftDelete:false</c>——物理删除（再生成全量替换 / Disable 级联清理）。</para>
/// <para>恢复码 SHA256 落库（明文不落库）+ 单次消费（<c>MarkConsumedAsync</c>——非并发路径）——
/// <c>MarkConsumedIfActiveAsync</c> 原子条件消费（WHERE IsConsumed=false 守卫，Oracle C1）——
/// <b>数据访问红线逃生口</b>（Oracle 裁决路径 B，DataPort raw SQL 先例）；验证纳入 per-user 频控（Oracle C4）。</para>
/// <para>⚠️ <c>[DiContractIgnore]</c>（V4.10.21 ADR71）：构造参数 <see cref="IFreeSql"/> 为逃生口（非领域服务
/// 接口、框架内置白名单外）——SG1a DI001 校验豁免标注（纯警告豁免，不改注册语义；逃生口裁决记录见 README/开发方案）。</para>
/// </summary>
[TKW.Framework.CodeGeneration.DiContractIgnore]
partial class MfaRecoveryCodeEntityDataService(IDomainUser user, IEntityDAC<MfaRecoveryCodeEntity> dac, IFreeSql fsql)
    : DomainDataServiceBase<MfaRecoveryCodeEntity, MfaRecoveryCodeEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>裸 ORM 逃生口（Oracle 裁决路径 B——仅承载原子条件更新，不做常规数据访问）。</summary>
    private readonly IFreeSql _fsql = fsql;
    // ── Service 委托路径的业务方法 ──

    /// <summary>按用户 + 码哈希查恢复码（VerifyRecoveryCode 入口——未消费）。</summary>
    public Task<MfaRecoveryCodeEntity?> GetByCodeHashAsync(string userId, string codeHash, CancellationToken ct = default)
        => EntityGetAsync(f => f.UserId == userId && f.CodeHash == codeHash && !f.IsConsumed, ct);

    /// <summary>按用户查全部恢复码（再生成全量替换——删旧插新 / Disable 级联清理）。</summary>
    public async Task<IReadOnlyList<MfaRecoveryCodeEntity>> GetByUserAsync(string userId, CancellationToken ct = default)
        => await EntitySelectAsync(f => f.UserId == userId, 0, MaxQuerySize, q => q.OrderBy(f => f.Id), ct);

    /// <summary>新增恢复码。</summary>
    public Task<MfaRecoveryCodeEntity> CreateAsync(MfaRecoveryCodeEntity entity, CancellationToken ct = default)
        => EntityCreateAsync(entity, ct);

    /// <summary>单次消费：翻转 IsConsumed=true（成功使用后调用——非并发路径保留）。</summary>
    public Task<MfaRecoveryCodeEntity> MarkConsumedAsync(long id, CancellationToken ct = default)
        => EntityUpdateAsync(new MfaRecoveryCodeEntity { Id = id, IsConsumed = true }, ct);

    /// <summary>
    /// 原子条件消费：WHERE IsConsumed=false 守卫（Oracle C1——并发验证恰一成功，防重放）。
    /// <para>返回 1 = 成功（本请求获得独占消费）；0 = 败（已被并发消费——调用方按失败处理）。</para>
    /// <para>数据访问红线逃生口（Oracle 裁决路径 B）：IEntityDAC 按主键无条件 UPDATE 无法表达竞争守卫，
    /// 引擎级单语句条件 UPDATE 天然原子（DataPort raw SQL 先例）；注入 IFreeSql 由 ActivatorUtilities 自动解析。
    /// 恢复码无 ExpireAt 列（无过期语义），故无过期谓词。</para>
    /// </summary>
    public async Task<int> MarkConsumedIfActiveAsync(long id, CancellationToken ct = default)
        => await _fsql.Update<MfaRecoveryCodeEntity>()
            .Set(x => x.IsConsumed, true)
            .Where(x => x.Id == id && !x.IsConsumed)
            .ExecuteAffrowsAsync(ct);

    /// <summary>按用户物理删除全部恢复码（再生成全量替换 / Disable 级联清理——循环批删防单批上限）。</summary>
    public async Task DeleteByUserAsync(string userId, CancellationToken ct = default)
    {
        while (true)
        {
            var batch = await EntitySelectAsync(f => f.UserId == userId, 0, DeleteBatchSize, q => q.OrderBy(f => f.Id), ct);
            if (batch.Count == 0) break;
            await EntityDeleteBatchAsync(batch.Select(f => f.Id), ct);
        }
    }

    /// <summary>查询批量上限（恢复码 8 枚低频，兜底）。</summary>
    private const int MaxQuerySize = 1000;

    /// <summary>删除批量步长（循环批删）。</summary>
    private const int DeleteBatchSize = 500;
}
