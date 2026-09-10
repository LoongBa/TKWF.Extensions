using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.SecurityLog;
using TKWF.Ext.SecurityLog.DTOs;

namespace TKWF.Ext.SecurityLog;

/// <summary>数据服务：安全日志表实体——记录认证/授权相关安全事件（登录成功/失败、登出、改密、密码重置、账户锁定、注册、挑战）。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 SecurityLogEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
//
// 【只增不改语义（Oracle C2）】
// 安全日志为追加写日志——本分部类刻意【不】提供任何 Update/Delete 业务方法（对比 AuditLogging 的
// AdminDeleteAsync），且 DataService 不标注 [GenerateController(FromDataService=true)]：
//   ① 无 Update/Delete 公开业务方法（.g.cs 内部转发访问器不构成业务方法，仅同程序集 Store 可用）；
//   ② 无 [GenerateController] → 不生成任何 REST/GraphQL 管理端点。
// 唯一写路径 = SecurityLogStore.SaveAsync → EntityCreateAsync（追加）。
// 查询计数直接用基类 DomainReadOnlyDataServiceBase.CountAsync（public virtual，QueryForUser 路径）——
// 不再手写 CountAsync（避免隐藏基类成员，且基类实现为 SQL COUNT 更高效）。
partial class SecurityLogEntityDataService(IDomainUser user, IEntityDAC<SecurityLogEntity> dac)
        : DomainDataServiceBase<SecurityLogEntity, SecurityLogEntityDto>(user, dac, hasSoftDelete:false)
{
    /// <summary>聚合拉取安全上限——内存 GroupBy 防全表拉取（对齐 Tagging GetFrequencyAsync Take(100_000) 先例）。</summary>
    private const int AggregationFetchLimit = 100_000;

    /// <summary>
    /// 失败次数 TopN 聚合（按用户名）——<see cref="ISecurityLogAnalyticsService.GetTopFailedUsersAsync"/> 委托路径。
    /// <para>实现说明（对齐 Tagging <c>GetFrequencyAsync</c> 范式，Oracle P1-3）：BackgroundJobs 已确认
    /// IQueryable 桥接不支持 GroupBy 翻译 → TopN 分组只能用<b>内存 GroupBy</b>——SQL WHERE 下推
    /// （Result==Failed + CreateTime 范围）+ <c>Dac.ToListAsync(Take(100_000))</c> 安全上限 + LINQ GroupBy +
    /// OrderByDescending(Count) + Take(topN)。</para>
    /// <para>空白 UserName 记录跳过（对齐 IP 聚合语义——空用户名不构成可审计维度，避免 "" 桶污染 TopN）。</para>
    /// </summary>
    /// <param name="fromUtc">起始时间（CreateTime &gt;=，闭区间下界；null = 不限）。</param>
    /// <param name="toUtc">结束时间（CreateTime &lt;=，闭区间上界；null = 不限）。</param>
    /// <param name="topN">返回条数（按 Count 降序取前 N；调用方保证 &gt; 0）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<List<SecurityLogFailureStat>> GetTopFailedByUserAsync(
        DateTime? fromUtc, DateTime? toUtc, int topN, CancellationToken ct = default)
    {
        var query = BuildFailedWindowQuery(fromUtc, toUtc);
        var rows = await Dac.ToListAsync(query.Take(AggregationFetchLimit), ct);
        return rows
            .Where(e => !string.IsNullOrWhiteSpace(e.UserName))
            .GroupBy(e => e.UserName)
            .Select(g => new SecurityLogFailureStat(g.Key, g.LongCount()))
            .OrderByDescending(s => s.Count)
            .Take(topN)
            .ToList();
    }

    /// <summary>
    /// 失败次数 TopN 聚合（按来源 IP）——<see cref="ISecurityLogAnalyticsService.GetTopFailedIpsAsync"/> 委托路径。
    /// <para>实现同 <see cref="GetTopFailedByUserAsync"/>（内存 GroupBy 范式）；<b>IpAddress 为 null/空白的记录跳过</b>
    /// （无来源 IP 的失败记录不构成 IP 维度，避免空桶污染 TopN）。</para>
    /// </summary>
    public async Task<List<SecurityLogFailureStat>> GetTopFailedByIpAsync(
        DateTime? fromUtc, DateTime? toUtc, int topN, CancellationToken ct = default)
    {
        var query = BuildFailedWindowQuery(fromUtc, toUtc);
        var rows = await Dac.ToListAsync(query.Take(AggregationFetchLimit), ct);
        return rows
            .Where(e => !string.IsNullOrWhiteSpace(e.IpAddress))
            .GroupBy(e => e.IpAddress!)
            .Select(g => new SecurityLogFailureStat(g.Key, g.LongCount()))
            .OrderByDescending(s => s.Count)
            .Take(topN)
            .ToList();
    }

    /// <summary>
    /// 保留天数清理——物理删除 <c>CreateTime &lt; cutoffUtc</c> 的过期记录（分批）。
    /// <para><b>决策记录（打破"只增不改" Oracle C2）</b>：安全日志默认永久留存，但合规留存窗口（默认 90 天）下
    /// 过期记录膨胀 Storage。本方法引入<b>删除路径</b>，限定为：
    /// ① 仅 <c>EntityDeleteBatchAsync</c> 物理删（<c>hasSoftDelete:false</c>——<c>EntitySoftDeleteAsync</c>
    /// 对未启用软删实体抛 <c>InvalidOperationException</c>，故绝不能用）；② 单点（仅本方法），无管理端点/单条删除；
    /// ③ <c>CreateTime</c> 仍 <c>CanUpdate=false</c>（列级不可改）。主框架 ADR 待用户许可后补录，
    /// 本次以代码注释 + README 承担决策记录职责。</para>
    /// <para>实现：先查过期 Id 列表（<c>Take(batchSize)</c>——分批防长事务/大锁），再 <c>EntityDeleteBatchAsync(ids)</c>。
    /// 返回本批删除条数（调用方循环直至 &lt; batchSize）。</para>
    /// </summary>
    /// <param name="cutoffUtc">截止时间（UTC）——仅删 <c>CreateTime &lt; cutoffUtc</c> 的记录。</param>
    /// <param name="batchSize">单批最大删除条数（&gt; 0）。</param>
    public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default)
    {
        var query = QueryForUser().Where(e => e.CreateTime < cutoffUtc).Take(batchSize);
        var rows = await Dac.ToListAsync(query, ct);
        if (rows.Count == 0) return 0;
        return await EntityDeleteBatchAsync(rows.Select(r => r.Id), ct);
    }

    /// <summary>构建失败聚合的 SQL WHERE 下推查询——Result==Failed + 可选 CreateTime 闭区间。
    /// <para>每个聚合独立经 <see cref="Query"/> 起新查询（FreeSql ISelect 原地可变陷阱——不跨调用复用）。</para></summary>
    private IQueryable<SecurityLogEntity> BuildFailedWindowQuery(DateTime? fromUtc, DateTime? toUtc)
    {
        var query = QueryForUser().Where(e => e.Result == SecurityLogEventTypes.ResultFailed);
        if (fromUtc.HasValue) query = query.Where(e => e.CreateTime >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(e => e.CreateTime <= toUtc.Value);
        return query;
    }
}
