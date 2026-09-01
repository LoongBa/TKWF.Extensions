using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// 二进制存储扩展专用用户类型——继承 <see cref="SimpleUserInfo"/>。
    /// <para>扩展不知道消费方 UserInfo 类型，用通用 SimpleUserInfo 作为默认泛型参数。</para>
    /// </summary>
    public class BlobStoringUserInfo : SimpleUserInfo
    {
        public BlobStoringUserInfo() : base() { }
    }
}
