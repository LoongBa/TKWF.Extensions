using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// 邮件发送扩展专用用户类型——继承 <see cref="SimpleUserInfo"/>。
    /// <para>扩展不知道消费方 UserInfo 类型，用通用 SimpleUserInfo 作为默认泛型参数。</para>
    /// </summary>
    public class EmailingUserInfo : SimpleUserInfo
    {
        public EmailingUserInfo() : base() { }
    }
}
