using System;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志列表项 DTO——从 <see cref="SecurityLogEntity"/> 投影，
    /// <b>不含 <c>Detail</c> 全文</b>（安全决策 D5：Detail 含异常消息，列表查询不拉大字段/敏感信息，对齐 AuditLogging 先例）。
    /// 详情经 <see cref="SecurityLogDetailDto"/>（GetDetailAsync）取全量。
    /// </summary>
    public record SecurityLogListItemDto
    {
        /// <summary>主键。</summary>
        public long Id { get; init; }

        /// <summary>事件类型（Login/Logout/PasswordChange/PasswordReset/Lockout/Register/Challenge）。</summary>
        public string EventType { get; init; } = "";

        /// <summary>事件分类（Authentication/Authorization）。</summary>
        public string EventCategory { get; init; } = "";

        /// <summary>尝试用户名。</summary>
        public string UserName { get; init; } = "";

        /// <summary>用户 ID（未认证为 null）。</summary>
        public long? UserId { get; init; }

        /// <summary>客户端 IP。</summary>
        public string? IpAddress { get; init; }

        /// <summary>结果（Success/Failed）。</summary>
        public string Result { get; init; } = "";

        /// <summary>记录创建时间（UTC）。</summary>
        public DateTime CreateTime { get; init; }
    }
}
