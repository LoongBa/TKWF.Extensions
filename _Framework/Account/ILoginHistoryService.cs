using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 登录历史与异常检测查询服务接口（V0.3.0）——消费 SecurityLog 扩展查询 API。
    /// <para><b>划界</b>：SecurityLog 是登录历史的唯一数据源（Account 不重复建表）；本服务仅委托
    /// <see cref="TKWF.Ext.SecurityLog.ISecurityLogQueryService"/> 与
    /// <see cref="TKWF.Ext.SecurityLog.ISecurityLogAnalyticsService"/>（经 IServiceProvider 延迟解析）。</para>
    /// <para><b>异常语义</b>：SecurityLog 扩展未启用（契约服务未注册）→ 明确抛
    /// <see cref="InvalidOperationException"/>（提示须启用 SecurityLog 扩展）；查询/聚合失败 → 委托方
    /// 已静默返回空（本服务外层兜底 Warning + 空结果，不抛异常）。</para>
    /// </summary>
    public interface ILoginHistoryService
    {
        /// <summary>
        /// 分页查询登录历史（EventType 固定 "Login"）。
        /// <para>委托 <see cref="TKWF.Ext.SecurityLog.ISecurityLogQueryService.GetListAsync"/>（列表 DTO 不含 Detail/UserAgent），
        /// 返回按登录时间倒序排列的登录事件投影。</para>
        /// </summary>
        /// <param name="input">查询条件（所有过滤字段可选，空条件 = 全量分页）。</param>
        /// <param name="ct">取消令牌。</param>
        Task<LoginHistoryPagedResult> GetLoginHistoryAsync(LoginHistoryQueryInput input, CancellationToken ct = default);

        /// <summary>
        /// 窗口内登录失败次数 TopN（按尝试用户名）——异常检测（暴力破解/撞库目标）。
        /// <para>委托 <see cref="TKWF.Ext.SecurityLog.ISecurityLogAnalyticsService.GetTopFailedUsersAsync"/>；
        /// 仅计 Result=="Failed" 记录，空白 UserName 跳过。</para>
        /// </summary>
        /// <param name="topN">返回条数（默认 10，按 Count 降序；&lt;=0 回退默认，上限 100）。</param>
        /// <param name="window">时间窗口（相对 UtcNow 的起止区间）；null = 全量（不限制时间）。</param>
        /// <param name="ct">取消令牌。</param>
        Task<IReadOnlyList<LoginFailureStat>> GetTopFailedUsersAsync(
            int topN = 10, TimeSpan? window = null, CancellationToken ct = default);

        /// <summary>
        /// 窗口内登录失败次数 TopN（按来源 IP）——异常检测（扫描/爆破源）。
        /// <para>委托 <see cref="TKWF.Ext.SecurityLog.ISecurityLogAnalyticsService.GetTopFailedIpsAsync"/>；
        /// 仅计 Result=="Failed" 记录，IpAddress 为 null/空白跳过。</para>
        /// </summary>
        Task<IReadOnlyList<LoginFailureStat>> GetTopFailedIpsAsync(
            int topN = 10, TimeSpan? window = null, CancellationToken ct = default);
    }
}
