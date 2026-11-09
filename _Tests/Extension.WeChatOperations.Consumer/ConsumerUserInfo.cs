using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.WeChatOperations.Consumer.Tests;

/// <summary>
/// V0.10.0（WARN009 契约形状纠正）：消费方自定义用户类型——模拟真实消费方（DMP）定义自己的 UserInfo。
/// <para>扩展 <see cref="WeChatOperationsExtensionInitializer{TUserInfo}"/> 以消费方 TUserInfo 泛型化接线；
/// 此处继承 <see cref="SimpleUserInfo"/>（对齐 Permissions.Consumer 先例）。</para>
/// </summary>
public class ConsumerUserInfo : SimpleUserInfo
{
    public ConsumerUserInfo() : base() { }

    public ConsumerUserInfo(string userIdString, string userName, params string[] roles)
        : base(userIdString, userName)
    {
        Roles = roles.ToList();
    }
}