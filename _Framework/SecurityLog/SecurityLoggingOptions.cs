using System;
using System.Collections.Generic;
using TKW.Framework.Domain;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志配置选项——<c>[Options("TKWF:SecurityLog")]</c> 声明配置节，SG1 生成绑定代码
    /// （<c>services.Configure&lt;SecurityLoggingOptions&gt;(configuration.GetSection("TKWF:SecurityLog"))</c>）+ 结构校验。
    /// </summary>
    [Options("TKWF:SecurityLog")]
    public class SecurityLoggingOptions
    {
        /// <summary>
        /// 是否启用安全日志采集（默认 true）。false 时过滤器零开销——直接返回，不解析 Store/不构造事件。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 启用的事件类型集合（空 = 全部启用）。
        /// 合法值：Login / Logout / PasswordChange / PasswordReset / Lockout / Register / Challenge。
        /// </summary>
        public HashSet<string> EventTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 安全日志保留天数（v0.2.0 保留天数清理）——<see cref="SecurityLogAnalyticsService.CleanupExpiredAsync"/>
        /// 删除 <c>CreateTime &lt; UtcNow - RetentionDays</c> 的过期记录。默认 90 天。
        /// <para>注意：该清理引入物理删除，打破"只增不改"（Oracle C2）语义——属合规留存窗口的运维必需，
        /// 限定 DataService <c>DeleteExpiredAsync</c> 单点物理删（hasSoftDelete:false），不暴露管理端点。
        /// 主框架 ADR 待用户许可后补录。</para>
        /// </summary>
        public int RetentionDays { get; set; } = 90;

        /// <summary>
        /// 保留清理单批删除条数（v0.2.0）——分批删除防长事务/大锁（每批 <see cref="SecurityLogEntityDataService.DeleteExpiredAsync"/>
        /// 查询 Take 后删除，循环至清完）。默认 500。
        /// </summary>
        public int CleanupBatchSize { get; set; } = 500;
    }
}
