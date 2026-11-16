using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.SmsCode.DTOs;

namespace TKWF.Ext.SmsCode;

/// <summary>
/// 数据服务：短信验证码记录实体——频控数据源 + 作废机制的持久化原子方法。
/// <para>提示：标准 CRUD 逻辑和构造函数已由 <c>SmsCodeRecordEntityDataService.g.cs</c> 承载，本分部仅编写业务原子方法。</para>
/// <para><b>只增不改语义</b>（对齐 AuthCenter SmsRecord 先例）：记录为追加写数据源——写路径 = Create（发送）/
/// MarkVerified（校验消费，仅 IsVerified 列 CAS）/ IncrementFailedAttempts（失败计数 CAS）/ InvalidateActive（重发作废旧码）。
/// 本分部类刻意【不】提供任何公开 Update/Delete 业务方法。</para>
/// <para><b>M3 闭环（评审）</b>：作废机制 = <c>FailedAttempts</c> 列 CAS 自增（&gt;=阈值 → 该码 ExpireAt 置 now）；
/// 重发作废旧码 = CAS 将 (Subject,Scene) 全部未消费未过期记录 ExpireAt 置 now —— 均用 <see cref="EntityUpdateWhereAsync"/>（ADR89 引擎级条件 UPDATE）。</para>
/// </summary>
partial class SmsCodeRecordEntityDataService(IDomainUser user, IEntityDAC<SmsCodeRecordEntity> dac)
        : DomainDataServiceBase<SmsCodeRecordEntity, SmsCodeRecordEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>追加记录一条验证码记录（发送频控数据源；CodeHash 为 SHA256 hex 小写，不存明文）。</summary>
    public async Task<SmsCodeRecordEntity> CreateAsync(SmsCodeRecordEntity entity, CancellationToken ct = default)
        => await EntityCreateAsync(entity, ct);

    /// <summary>最新未消费验证码（IsVerified==false，CreateTime 降序取最新一条——不过滤过期，过期与否由门面判定区分 NotFound/Expired 语义）。</summary>
    public async Task<SmsCodeRecordEntity?> GetLatestUnverifiedAsync(string subject, string scene, CancellationToken ct = default)
    {
        var rows = await EntitySelectAsync(
            e => e.Subject == subject && e.Scene == scene && !e.IsVerified,
            0, 1,
            q => q.OrderByDescending(e => e.CreateTime),
            ct);
        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>发送次数计数（重发间隔/小时/日频控——SQL COUNT 下推，对齐基类 CountAsync 路径）。</summary>
    public async Task<int> CountSentAsync(string subject, string scene, DateTime fromUtc, CancellationToken ct = default)
        => (int)await CountAsync(e => e.Subject == subject && e.Scene == scene && e.CreateTime >= fromUtc, ct);

    /// <summary>
    /// 频控窗口内【最老】记录创建时间（滑动窗口 retryAfter 计算——"距最严约束重置秒数"= oldest+window-now；
    /// 审核 P2 修复：旧实现在命中时给"窗长"或"到午夜"，非真实重置时刻）。
    /// </summary>
    /// <returns>窗口内最老记录 CreateTime；窗口内无记录则 null（理论不达——命中即≥1 条）。</returns>
    public async Task<DateTime?> GetOldestInWindowAsync(string subject, string scene, DateTime fromUtc, CancellationToken ct = default)
    {
        var rows = await EntitySelectAsync(
            e => e.Subject == subject && e.Scene == scene && e.CreateTime >= fromUtc,
            0, 1,
            q => q.OrderBy(e => e.CreateTime),
            ct);
        return rows.Count > 0 ? rows[0].CreateTime : null;
    }

    /// <summary>活跃未消费码计数（MaxActiveCodesPerSubject——只统计未失效记录；重发作废旧码机制保证旧码已失效不膨胀计数）。</summary>
    public async Task<int> CountActiveAsync(string subject, string scene, CancellationToken ct = default)
        => (int)await CountAsync(
            e => e.Subject == subject && e.Scene == scene && !e.IsVerified && e.ExpireAt > DateTime.UtcNow,
            ct);

    /// <summary>标记验证码已消费（IsVerified=true，单次消费——校验成功路径）。</summary>
    /// <returns>标记成功 true；记录不存在/已消费 false。</returns>
    public async Task<bool> MarkVerifiedAsync(long id, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            where: e => e.Id == id && !e.IsVerified,
            setColumns: e => new SmsCodeRecordEntity { IsVerified = true },
            ct: ct) > 0;

    /// <summary>
    /// 失败计数 CAS 自增（FailedAttempts 列，null=0 语义）——M3 作废机制：连续失败达到阈值即作废该码。
    /// <para>调用方读回更新后计数判定（重查或读返回值）；防 read-modify-write 竞态（ADR89）。</para>
    /// </summary>
    /// <returns>影响行数（1=自增成功；0=记录不存在）。</returns>
    public async Task<int> IncrementFailedAttemptsAsync(long id, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            where: e => e.Id == id && !e.IsVerified,
            setColumns: e => new SmsCodeRecordEntity
            {
                FailedAttempts = e.FailedAttempts == null ? 1 : e.FailedAttempts + 1
            },
            ct: ct);

    /// <summary>
    /// 重发作废旧码——CAS 将 (Subject,Scene) 全部未消费未过期记录的 ExpireAt 置 now（M3 闭环）。
    /// <para>【不是】verify-latest-only（后者导致活跃计数膨胀）；旧码即时失效后 <see cref="CountActiveAsync"/> 只统计未失效记录。</para>
    /// </summary>
    /// <returns>作废行数。</returns>
    public async Task<int> InvalidateActiveAsync(string subject, string scene, CancellationToken ct = default)
        => await EntityUpdateWhereAsync(
            where: e => e.Subject == subject && e.Scene == scene && !e.IsVerified && e.ExpireAt > DateTime.UtcNow,
            setColumns: e => new SmsCodeRecordEntity { ExpireAt = DateTime.UtcNow },
            ct: ct);
}