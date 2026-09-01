using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志扩展专用用户类型——继承 <see cref="SimpleUserInfo"/>。
    /// <para>扩展不知道消费方 UserInfo 类型，用通用 SimpleUserInfo 作为默认泛型参数。
    /// 消费方在注册 FilterBuilder/AddAuditLog 时可指定自己的 TUserInfo。</para>
    /// </summary>
    public class AuditLoggingUserInfo : SimpleUserInfo
    {
        public AuditLoggingUserInfo() : base() { }
    }
}
