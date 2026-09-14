using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Metrics.Tests;
using TKWF.Ext.Metrics.Tests.DTOs;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// 测试持久化实体 DataService 业务方法分部（消费方模拟，标准管线）。
/// <para>标准 CRUD 基座 + internal 原子转发已由 <c>TestMetricResultEntityDataService.g.cs</c>（xCodeGen 生成）承载；
/// 本分部只编写<b>业务方法</b>（public 委托包装，对齐扩展项目固有模式 AuditLogEntityDataService.cs + .g.cs 分部对）。
/// 训练消费方委托路径——红线合规：Store 只经本 DataService public 方法访问数据。</para>
/// </summary>
public partial class TestMetricResultEntityDataService(IDomainUser user, IEntityDAC<TestMetricResultEntity> dac)
    : DomainDataServiceBase<TestMetricResultEntity, TestMetricResultEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>批量创建（public 委托——Store SaveAsync 写路径；调生成 internal 转发 EntityCreateBatchAsync）。</summary>
    public async Task CreateBatchAsync(IReadOnlyList<TestMetricResultEntity> entities, CancellationToken ct = default)
        => await EntityCreateBatchAsync(entities, ct);

    /// <summary>
    /// 按指标过滤查询（分页，CalculatedAtUtc 降序）——public 委托——Store QueryAsync 读路径。
    /// <para>对齐方案 §3.4 示例 ② <c>EntitySelectAsync(BuildPredicate(...), skip, take, orderBy, ct)</c>。</para>
    /// </summary>
    public async Task<List<TestMetricResultEntity>> QueryByMetricAsync(
        string? specKey, string? name, DateTime? fromUtc, DateTime? toUtc,
        int skip, int take, CancellationToken ct = default)
        => await EntitySelectAsync(
            BuildPredicate(specKey, name, fromUtc, toUtc), skip, take,
            q => q.OrderByDescending(e => e.CalculatedAtUtc), ct);

    /// <summary>同 QueryByMetricAsync 条件计数（public 委托——Store CountAsync；基类 CountAsync 谓词重载）。</summary>
    public async Task<long> CountByMetricAsync(
        string? specKey, string? name, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
        => await CountAsync(BuildPredicate(specKey, name, fromUtc, toUtc), ct);

    /// <summary>
    /// 保留天数清理——物理删除 <c>CalculatedAtUtc &lt; cutoffUtc</c> 的过期记录（循环分批清完）。
    /// <para><b>C3 语义</b>：<c>while(true){ select 一批; if empty break; delete; total+=; }</c>——不清完不返回
    /// （对齐 SecurityLogAnalyticsService 先例）；竞态由下次调用补删（best-effort）。物理删
    /// （hasSoftDelete:false——调生成 internal 转发 EntityDeleteBatchAsync，不经软删分派）。</para>
    /// </summary>
    public async Task<int> DeleteBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default)
    {
        var batch = Math.Max(1, batchSize);
        var total = 0;
        while (true)
        {
            var ids = await EntitySelectAsync(e => e.CalculatedAtUtc < cutoffUtc, 0, batch, ct: ct);
            if (ids.Count == 0) break;
            total += await EntityDeleteBatchAsync(ids.Select(e => e.Id), ct);
            if (ids.Count < batch) break;
        }
        return total;
    }

    /// <summary>
    /// 构建查询谓词——动态组合 SpecKey/Name 精确 + FromUtc/ToUtc 计算时间闭区间（对齐方案 §3.4 BuildPredicate 语义）。
    /// <para>Expression API 动态组合（对齐 AuditLogEntityDataService.BuildQueryPredicate 先例）——避免闭包捕获
    /// 局部变量的翻译问题，全部常量折入表达式树。</para>
    /// </summary>
    private static Expression<Func<TestMetricResultEntity, bool>>? BuildPredicate(
        string? specKey, string? name, DateTime? fromUtc, DateTime? toUtc)
    {
        var param = Expression.Parameter(typeof(TestMetricResultEntity), "e");
        Expression? combined = null;

        if (!string.IsNullOrEmpty(specKey))
            combined = Combine(combined, Expression.Equal(
                Expression.Property(param, nameof(TestMetricResultEntity.SpecKey)),
                Expression.Constant(specKey)));

        if (!string.IsNullOrEmpty(name))
            combined = Combine(combined, Expression.Equal(
                Expression.Property(param, nameof(TestMetricResultEntity.Name)),
                Expression.Constant(name)));

        if (fromUtc.HasValue)
            combined = Combine(combined, Expression.GreaterThanOrEqual(
                Expression.Property(param, nameof(TestMetricResultEntity.CalculatedAtUtc)),
                Expression.Constant(fromUtc.Value)));

        if (toUtc.HasValue)
            combined = Combine(combined, Expression.LessThanOrEqual(
                Expression.Property(param, nameof(TestMetricResultEntity.CalculatedAtUtc)),
                Expression.Constant(toUtc.Value)));

        return combined == null ? null : Expression.Lambda<Func<TestMetricResultEntity, bool>>(combined, param);
    }

    private static Expression Combine(Expression? left, Expression right)
        => left == null ? right : Expression.AndAlso(left, right);
}