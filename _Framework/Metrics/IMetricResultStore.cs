using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Metrics
{
    /// <summary>
    /// 指标结果持久化契约——保存/查询/计数/清理四类标准化操作。
    /// <para>契约全部围绕 <see cref="MetricResultRow"/>（标准化行）/ <see cref="MetricResultQuery"/>（查询条件），
    /// 不出现任何实体类型——扩展不感知消费方表结构（ADR-Metrics-指标结果持久化契约与实体归属 决策 3）。
    /// 实现由<b>消费方</b>提供（实体形态消费方自定，扩展不注册默认实现——ADR 决策 1）。</para>
    /// <para>红线合规：消费方实现必须委托自己的 SG 生成 DataService（public 委托方法），
    /// 不注入 IFreeSql / IEntityDAC 裸写数据访问（数据访问红线 2026-09-07 裁定）。</para>
    /// </summary>
    public interface IMetricResultStore
    {
        /// <summary>
        /// 保存指标结果（标准化行批量落库）。返回影响行数。
        /// <para>实现方将 <see cref="MetricResultRow"/> 映射为自身实体（写路径普通实体经
        /// DataService 批量创建）；空列表应幂等返回 0。</para>
        /// </summary>
        /// <param name="rows">标准化行列表。</param>
        /// <param name="ct">取消令牌。</param>
        Task<int> SaveAsync(IReadOnlyList<MetricResultRow> rows, CancellationToken ct = default);

        /// <summary>
        /// 查询指标结果（分页/过滤，标准化行返回）。
        /// <para>语义：SpecKey/Name 精确匹配；FromUtc/ToUtc 计算时间闭区间；<see cref="MetricResultQuery.Take"/>
        /// 实现方 MUST 钳制到 [1,200]（静默）；<see cref="MetricResultQuery.DimensionFilter"/> 为可选增强
        /// （未实现时返回未按维度过滤的结果，兼容降级）。</para>
        /// </summary>
        /// <param name="query">查询条件。</param>
        /// <param name="ct">取消令牌。</param>
        Task<IReadOnlyList<MetricResultRow>> QueryAsync(MetricResultQuery query, CancellationToken ct = default);

        /// <summary>
        /// 指标结果计数（同 <see cref="QueryAsync"/> 查询条件）。
        /// </summary>
        /// <param name="query">查询条件。</param>
        /// <param name="ct">取消令牌。</param>
        Task<long> CountAsync(MetricResultQuery query, CancellationToken ct = default);

        /// <summary>
        /// 清理过期指标结果（保留天数，分批删除）。返回删除条数。
        /// <para><b>语义（C3，best-effort 一次调用清完当前过期集）</b>：实现方循环分批——
        /// <c>while (true) { ids = select batch (CalculatedAtUtc &lt; cutoff); if empty break; total += delete(ids); }</c>
        /// 对齐 SecurityLogAnalyticsService 先例，<b>不清完不返回</b>（非单批即停）。竞态（select→delete 之间
        /// 并发插入/删除）可致漏删或部分失败——best-effort 可接受（物理删幂等，cutoff 为 &lt; 闭区间，
        /// 下次调用补删；调用方按需经 BackgroundJobs/Quartz 定时重调）。</para>
        /// </summary>
        /// <param name="retentionDays">保留天数——删除 <c>CalculatedAtUtc &lt; UtcNow - retentionDays</c> 的行。</param>
        /// <param name="batchSize">单批最大删除条数（默认 500）。</param>
        /// <param name="ct">取消令牌。</param>
        Task<int> CleanupExpiredAsync(int retentionDays, int batchSize = 500, CancellationToken ct = default);
    }
}