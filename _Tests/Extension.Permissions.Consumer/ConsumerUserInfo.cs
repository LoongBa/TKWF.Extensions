using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Permissions.Consumer.Tests;

/// <summary>
/// V0.7.0 (W4)：消费方自定义用户类型——模拟真实消费方定义自己的 UserInfo。
/// <para>消费方拥有自己的用户身份（如 DMP 的 DMPUserInfo），扩展 <see cref="PermissionExtensionInitializer{TUserInfo}"/>
/// 以消费方 TUserInfo 泛型化接线。此处继承 <see cref="SimpleUserInfo"/> 并扩展消费方专属属性。</para>
/// </summary>
public class ConsumerUserInfo : SimpleUserInfo
{
    public ConsumerUserInfo() : base() { }

    public ConsumerUserInfo(string userIdString, string userName, params string[] roles)
        : base(userIdString, userName)
    {
        Roles = roles.ToList();
    }

    /// <summary>消费方专属：部门编号。</summary>
    public string DepartmentCode { get; set; } = "";
}
