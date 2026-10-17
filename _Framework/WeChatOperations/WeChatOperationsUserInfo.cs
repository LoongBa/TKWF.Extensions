using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.WeChatOperations
{
    /// <summary>
    /// 微信公众号运营扩展专用用户类型——继承 <see cref="SimpleUserInfo"/>。
    /// <para>扩展不知道消费方 UserInfo 类型，用通用 SimpleUserInfo 作为默认泛型参数
    /// （对齐 SettingsUserInfo 先例——扩展实体 UserType 由宿主初始化器推断，ADR43 D3，不在此指定）。</para>
    /// </summary>
    public class WeChatOperationsUserInfo : SimpleUserInfo
    {
        public WeChatOperationsUserInfo() : base() { }
    }
}