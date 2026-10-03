using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;

namespace TKWF.Ext.Testing.Shared;

/// <summary>
/// 消费方最小用户助手（测试不实际登录，仅满足抽象方法）。
/// C 基座公共类（2026-10-04）：统一 26 个测试项目的标准 <c>TestUserHelper</c> 定义。
/// </summary>
public sealed class TestUserHelper : DomainUserHelperBase<TestUserInfo>
{
    protected override Task<TestUserInfo> OnNewGuestSessionCreatedAsync(SessionInfo session)
        => Task.FromResult(new TestUserInfo("guest", "Guest"));

    protected override Task<TestUserInfo> OnLoginByPasswordAsync(
        DomainUser<TestUserInfo> user, string userName, string credential, EnumLoginFrom loginFrom)
        => Task.FromResult(new TestUserInfo(userName, userName));
}