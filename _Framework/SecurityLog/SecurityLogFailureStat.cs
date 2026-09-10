namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 失败次数聚合统计（v0.2.0 异常检测）——<see cref="ISecurityLogAnalyticsService"/> 输出项。
    /// <para>由 <see cref="SecurityLogEntityDataService.GetTopFailedByUserAsync"/>（<paramref name="Dimension"/> = UserName）
    /// / <see cref="SecurityLogEntityDataService.GetTopFailedByIpAsync"/>（<paramref name="Dimension"/> = IpAddress）聚合产出——
    /// 仅计 <c>Result == "Failed"</c> 记录，按 Count 降序。</para>
    /// </summary>
    /// <param name="Dimension">聚合维度值（用户名 / 来源 IP）。</param>
    /// <param name="Count">窗口内失败次数。</param>
    public sealed record SecurityLogFailureStat(string Dimension, long Count);
}