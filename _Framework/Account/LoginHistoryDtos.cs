using System;
using System.Collections.Generic;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 登录历史查询输入参数（V0.3.0）——对齐 SecurityLog 过滤维度（UserName/IP/Result/时间范围 + 分页）。
    /// <para>查询经 <see cref="ILoginHistoryService.GetLoginHistoryAsync"/> 转发为
    /// <c>SecurityLogQueryInput</c>（EventType 固定 "Login"）。所有过滤字段可选，支持任意组合。</para>
    /// </summary>
    public record LoginHistoryQueryInput
    {
        /// <summary>尝试用户名（LIKE '%value%' 模糊匹配）。null = 不限。</summary>
        public string? UserName { get; init; }

        /// <summary>客户端 IP（LIKE '%value%' 模糊匹配）。null = 不限。</summary>
        public string? IpAddress { get; init; }

        /// <summary>结果（精确匹配：Success/Failed）。null = 不限。</summary>
        public string? Result { get; init; }

        /// <summary>登录时间范围起始（UTC，含）。null = 不限。</summary>
        public DateTime? FromUtc { get; init; }

        /// <summary>登录时间范围结束（UTC，含）。null = 不限。</summary>
        public DateTime? ToUtc { get; init; }

        /// <summary>跳过前 N 条记录（分页偏移量）。默认 0。</summary>
        public int Skip { get; init; }

        /// <summary>返回记录数上限。默认 50，最大 200（防滥用，对齐 SecurityLog）。</summary>
        public int Take { get; init; } = 50;
    }

    /// <summary>
    /// 登录历史列表项 DTO——从 <see cref="TKWF.Ext.SecurityLog.SecurityLogListItemDto"/> 投影。
    /// <para><b>UserAgent 说明</b>：SecurityLog 列表 DTO（D5 安全决策）不含 <c>UserAgent</c> 字段——
    /// 列表投影恒为 null；含 UA 的详情语义由 SecurityLog <c>GetDetailAsync</c> 承载（本服务不提供详情端点）。</para>
    /// </summary>
    public record LoginHistoryItemDto
    {
        /// <summary>主键（SecurityLog 记录 Id）。</summary>
        public long Id { get; init; }

        /// <summary>尝试用户名。</summary>
        public string UserName { get; init; } = "";

        /// <summary>用户 ID（未认证为 null）。</summary>
        public long? UserId { get; init; }

        /// <summary>客户端 IP。</summary>
        public string? IpAddress { get; init; }

        /// <summary>客户端 UserAgent（列表投影恒 null——见类型注释）。</summary>
        public string? UserAgent { get; init; }

        /// <summary>结果（Success/Failed）。</summary>
        public string Result { get; init; } = "";

        /// <summary>登录时间（UTC）。</summary>
        public DateTime CreateTime { get; init; }
    }

    /// <summary>登录历史分页查询结果——包含总数与当前页 DTO 列表。</summary>
    /// <param name="Total">符合条件的总记录数（用于前端分页控件）。</param>
    /// <param name="Items">当前页 DTO 列表。</param>
    public record LoginHistoryPagedResult(long Total, IReadOnlyList<LoginHistoryItemDto> Items);

    /// <summary>
    /// 失败次数聚合统计（V0.3.0 异常检测）——<see cref="ILoginHistoryService"/> 输出项。
    /// <para>独立 record（不从 SecurityLog 复用 <c>SecurityLogFailureStat</c>）——避免跨扩展类型泄漏：
    /// Account 对外契约自持，经投影由 <c>SecurityLogFailureStat</c> 映射。</para>
    /// </summary>
    /// <param name="Dimension">聚合维度值（用户名 / 来源 IP）。</param>
    /// <param name="Count">窗口内失败次数。</param>
    public sealed record LoginFailureStat(string Dimension, long Count);
}
