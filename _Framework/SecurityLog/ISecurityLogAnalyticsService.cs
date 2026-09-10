using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志分析服务接口（v0.2.0 异常检测聚合 + 保留天数清理）——扩展侧自建，不修改主框架。
    /// <para><b>数据访问红线合规</b>：本服务只依赖 <see cref="SecurityLogEntityDataService"/>（SG1 DataService）委托——
    /// 绝不注入 IFreeSql / IEntityDAC；异常静默对齐扩展既有模式（失败记 Warning 日志，返回空结果/0，不抛异常）。</para>
    /// <para><b>边界</b>：聚合仅查不改（只读 TopN）；唯一的写路径是保留清理 <see cref="CleanupExpiredAsync"/>
    /// （打破"只增不改"语义的决策已记录——见 DataService <c>DeleteExpiredAsync</c> 注释与 README §七）。</para>
    /// </summary>
    public interface ISecurityLogAnalyticsService
    {
        /// <summary>
        /// 窗口内失败次数 TopN（按尝试用户名）。仅计 <c>Result=="Failed"</c> 记录；空白 UserName 跳过。
        /// 暴力破解/撞库检测：同一用户名高频失败 = 疑似撞库目标。
        /// </summary>
        /// <param name="topN">返回条数（默认 10，按 Count 降序；&lt;=0 回退默认，上限 100）。</param>
        /// <param name="window">时间窗口（相对 UtcNow 的起止区间）；null = 全量（不限制时间）。</param>
        /// <param name="ct">取消令牌。</param>
        Task<IReadOnlyList<SecurityLogFailureStat>> GetTopFailedUsersAsync(
            int topN = 10, TimeSpan? window = null, CancellationToken ct = default);

        /// <summary>
        /// 窗口内失败次数 TopN（按来源 IP）。仅计 <c>Result=="Failed"</c> 记录；IpAddress 为 null/空白跳过。
        /// 暴力破解/撞库检测：同一来源 IP 高频失败 = 疑似扫描/爆破源。
        /// </summary>
        Task<IReadOnlyList<SecurityLogFailureStat>> GetTopFailedIpsAsync(
            int topN = 10, TimeSpan? window = null, CancellationToken ct = default);

        /// <summary>
        /// 保留天数清理——删除 <c>CreateTime &lt; UtcNow - RetentionDays</c> 的过期记录（分批循环清完）。
        /// 配置见 <see cref="SecurityLoggingOptions.RetentionDays"/>（默认 90 天）与
        /// <see cref="SecurityLoggingOptions.CleanupBatchSize"/>（默认 500 条/批）。
        /// 返回实际删除总条数。可周期性调用（如每日后台任务）。
        /// </summary>
        Task<int> CleanupExpiredAsync(CancellationToken ct = default);
    }
}