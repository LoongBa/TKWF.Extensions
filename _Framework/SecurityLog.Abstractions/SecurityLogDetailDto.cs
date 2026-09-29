using System;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志详情 DTO——按 Id 取全量（含 Detail 全文、UserAgent、CorrelationId）。
    /// </summary>
    public record SecurityLogDetailDto
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

        /// <summary>客户端 UserAgent。</summary>
        public string? UserAgent { get; init; }

        /// <summary>结果（Success/Failed）。</summary>
        public string Result { get; init; } = "";

        /// <summary>详情（异常消息/返回值消息，脱敏——不含密码/令牌）。</summary>
        public string? Detail { get; init; }

        /// <summary>关联 ID（分布式链路追踪）。</summary>
        public string? CorrelationId { get; init; }

        /// <summary>记录创建时间（UTC）。</summary>
        public DateTime CreateTime { get; init; }
    }
}
