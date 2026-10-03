using TKW.Framework.Domain;

namespace TKWF.Ext.Testing.Shared;

/// <summary>
/// 消费方最小用户类型——模拟真实消费方定义自己的 UserInfo。
/// C 基座公共类（2026-10-04，框架组裁定 C 先行）：统一 26 个测试项目的标准 <c>TestUserInfo</c> 定义。
/// </summary>
public class TestUserInfo : SimpleUserInfo
{
    public TestUserInfo() : base() { }

    public TestUserInfo(string userIdString, string userName, params string[] roles)
        : base(userIdString, userName)
    {
        Roles = roles.ToList();
    }
}