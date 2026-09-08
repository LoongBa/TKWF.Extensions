using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志事件模型——由 <see cref="SecurityLogFilterAttribute{TUserInfo}"/> 采集构造，
    /// 经 <see cref="ISecurityLogStore"/> 落库（映射为 <see cref="SecurityLogEntity"/>）。
    /// </summary>
    /// <param name="EventType">事件类型：Login / Logout / PasswordChange / PasswordReset / Lockout / Register / Challenge。</param>
    /// <param name="EventCategory">事件分类：Authentication / Authorization（v0.1.0 均为 Authentication）。</param>
    /// <param name="UserName">尝试用户名（登录失败 = 请求输入，防枚举语义下保留审计来源）。</param>
    /// <param name="UserId">用户 ID（认证成功后回填，未认证为 null）。</param>
    /// <param name="IpAddress">客户端 IP（IAmbientContext["ClientIp"]）。</param>
    /// <param name="UserAgent">客户端 UserAgent（IAmbientContext["UserAgent"]，无则 null）。</param>
    /// <param name="Result">结果：Success / Failed。</param>
    /// <param name="Detail">详情——失败时记录脱敏异常消息（绝不含密码/令牌明文）。</param>
    /// <param name="CorrelationId">关联 ID（分布式链路追踪）。</param>
    public sealed record SecurityLogEntry(
        string EventType,
        string EventCategory,
        string UserName,
        long? UserId,
        string? IpAddress,
        string? UserAgent,
        string Result,
        string? Detail,
        string? CorrelationId);
}
