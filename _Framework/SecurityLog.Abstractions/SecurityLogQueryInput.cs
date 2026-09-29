using System;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志查询输入参数——所有过滤字段可选，支持任意组合。
    /// <para>分页使用 <see cref="Skip"/> + <see cref="Take"/>（默认 50，对齐 DefaultPageSize；上限 200 防滥用）。
    /// 时间范围作用于 <c>CreateTime</c>（UTC）。</para>
    /// </summary>
    public record SecurityLogQueryInput
    {
        /// <summary>尝试用户名（LIKE '%value%' 模糊匹配）。null = 不限。</summary>
        public string? UserName { get; init; }

        /// <summary>客户端 IP（LIKE '%value%' 模糊匹配——兼容 IPv4/IPv6/端口变体）。null = 不限。</summary>
        public string? IpAddress { get; init; }

        /// <summary>事件类型（精确匹配：Login/Logout/PasswordChange/PasswordReset/Lockout/Register/Challenge）。null = 不限。</summary>
        public string? EventType { get; init; }

        /// <summary>结果（精确匹配：Success/Failed）。null = 不限。</summary>
        public string? Result { get; init; }

        /// <summary>创建时间范围起始（UTC，含）。null = 不限。</summary>
        public DateTime? FromUtc { get; init; }

        /// <summary>创建时间范围结束（UTC，含）。null = 不限。</summary>
        public DateTime? ToUtc { get; init; }

        /// <summary>跳过前 N 条记录（分页偏移量）。默认 0。</summary>
        public int Skip { get; init; }

        /// <summary>返回记录数上限。默认 50，最大 200（防滥用）。</summary>
        public int Take { get; init; } = 50;
    }
}
