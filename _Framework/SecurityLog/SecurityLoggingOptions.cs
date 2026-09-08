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
    }
}
