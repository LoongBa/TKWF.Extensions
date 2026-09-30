using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
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
    /// <summary>
    /// 失败次数 TopN 聚合（按用户名）——<see cref="ISecurityLogAnalyticsService.GetTopFailedUsersAsync"/> 委托路径。
    /// <para>实现说明（V4.10.39 聚合 SQL 下推，框架 <c>GroupCountAsync</c>）：FreeSql LINQ 桥接不支持
    /// GroupBy 翻译（背景同 Tagging <c>GetFrequencyAsync</c> 分析），V4.10.39 起经 <c>RestoreToSelect()</c>
    /// 还原 ISelect 走原生分组聚合——SQL <c>GROUP BY UserName + COUNT(*) + ORDER BY COUNT(*) DESC + LIMIT n</c>
    /// 全下推，取代原"内存 GroupBy + Take(100_000) 安全上限"范式（不再拉全量）。</para>
    /// <para>Result==Failed + CreateTime 范围（<see cref="BuildFailedWindowQuery"/>）+ 空白 UserName 跳过
    /// （<c>!string.IsNullOrWhiteSpace</c>——空用户名不构成可审计维度，避免 "" 桶污染 TopN）全部下推 SQL。</para>
    /// </summary>
    /// <param name="fromUtc">起始时间（CreateTime &gt;=，闭区间下界；null = 不限）。</param>
    /// <param name="toUtc">结束时间（CreateTime &lt;=，闭区间上界；null = 不限）。</param>
    /// <param name="topN">返回条数（按 Count 降序取前 N；调用方保证 &gt; 0）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<List<SecurityLogFailureStat>> GetTopFailedByUserAsync(
        DateTime? fromUtc, DateTime? toUtc, int topN, CancellationToken ct = default)
    {
        // 空白键过滤下推 SQL（原内存 Where 后 GroupBy → SQL WHERE trim(UserName) <> ''，避免 "" 桶污染分组结果）
        var query = BuildFailedWindowQuery(fromUtc, toUtc)
            .Where(e => !string.IsNullOrWhiteSpace(e.UserName));
        var pairs = await query.GroupCountAsync(e => e.UserName, topN, ct);   // SQL GROUP BY + COUNT + ORDER BY COUNT DESC + LIMIT 全下推
        return pairs.Select(p => new SecurityLogFailureStat(p.Key, p.Value)).ToList();
    }

    /// <summary>
    /// 失败次数 TopN 聚合（按来源 IP）——<see cref="ISecurityLogAnalyticsService.GetTopFailedIpsAsync"/> 委托路径。
    /// <para>实现同 <see cref="GetTopFailedByUserAsync"/>（<c>GroupCountAsync</c> SQL 全下推范式）；
    /// <b>IpAddress 为 null/空白的记录跳过</b>（无来源 IP 的失败记录不构成 IP 维度，避免空桶污染 TopN，
    /// 过滤同样下推 SQL——<c>WHERE trim(IpAddress) &lt;&gt; ''</c>）。</para>
    /// </summary>
    public async Task<List<SecurityLogFailureStat>> GetTopFailedByIpAsync(
        DateTime? fromUtc, DateTime? toUtc, int topN, CancellationToken ct = default)
    {
        // IpAddress 可空——空白键过滤下推 SQL，keySelector 用 ! 收窄为 string
        var query = BuildFailedWindowQuery(fromUtc, toUtc)
            .Where(e => !string.IsNullOrWhiteSpace(e.IpAddress));
        var pairs = await query.GroupCountAsync(e => e.IpAddress!, topN, ct);
        return pairs.Select(p => new SecurityLogFailureStat(p.Key, p.Value)).ToList();
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
