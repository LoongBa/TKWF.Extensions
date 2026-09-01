using System;
using System.Collections.Generic;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志配置选项。
    /// <para>通过 <c>services.Configure&lt;AuditLoggingOptions&gt;(config.GetSection("TKWF:AuditLogging"))</c> 绑定。</para>
    /// </summary>
    public class AuditLoggingOptions
    {
        /// <summary>是否启用审计日志（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>是否为匿名调用记录审计日志（默认 false）。</summary>
        public bool LogAnonymous { get; set; } = false;

        /// <summary>是否序列化返回值到审计记录（默认 false，防止大对象/敏感返回值落盘）。</summary>
        public bool SaveReturnValues { get; set; } = false;

        /// <summary>附加敏感字段名集合（参数 JSON 序列化时值替换为 "***"）。</summary>
        public HashSet<string> AdditionalSensitiveFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
