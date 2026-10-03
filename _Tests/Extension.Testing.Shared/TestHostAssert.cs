using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Testing.Shared;

/// <summary>
/// 注册形态断言辅助（C 基座，2026-10-04）——对齐 A 精简版目标签名 <c>AssertConstructible</c>。
/// <para>验证 <c>AddConstructibleService</c> 门面注册三要素（转达文档 §6.1 A 精简版语义）：</para>
/// <list type="bullet">
/// <item>帧内 <c>User.Use&lt;TInterface&gt;()</c> AOP 可解析（守卫工厂经 CurrentAopUser 放行）；</item>
/// <item>实现类 <c>GetRequiredService&lt;TImpl&gt;()</c> 直解必抛（throw-factory 禁直取）；</item>
/// <item>帧外（UnBindScope 后）<c>GetRequiredService&lt;TInterface&gt;()</c> 必抛（CurrentAopUser 空守卫）。</item>
/// </list>
/// ⚠️ 安全设计硬约束：断言走**真实 AOP 解析路径**——禁自定义工厂 override、禁预构建实例注入、禁
/// ActivatorUtilities 直建（否则构成守卫语义的测试逃生口）。方法内显式抛 <see cref="InvalidOperationException"/>
/// 描述失败点（测试框架可断言），不静默。
/// </summary>
public static class TestHostAssert
{
    /// <summary>帧内 <c>User.Use&lt;TInterface&gt;()</c> 可解析（生产 AOP 路径）。</summary>
    public static void AssertConstructible<TInterface>(DomainUser<TestUserInfo> user)
        where TInterface : class, IDomainService
    {
        TInterface service;
        try
        {
            service = user.Use<TInterface>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[AssertConstructible] 帧内 User.Use<{typeof(TInterface).Name}>() 解析失败（守卫工厂/AOP 路径异常）：{ex.Message}", ex);
        }

        if (service is null)
            throw new InvalidOperationException(
                $"[AssertConstructible] 帧内 User.Use<{typeof(TInterface).Name}>() 返回 null");
    }

    /// <summary>实现类直解必抛（throw-factory 禁直取）。</summary>
    public static void AssertImplementationThrows<TImplementation>(ServiceProvider provider)
        where TImplementation : class
    {
        try
        {
            provider.GetRequiredService<TImplementation>();
        }
        catch (InvalidOperationException)
        {
            return; // 预期：throw-factory 抛 InvalidOperationException
        }
        throw new InvalidOperationException(
            $"[AssertConstructible] 实现类 {typeof(TImplementation).Name} 直解未抛（throw-factory 语义被绕过）");
    }

    /// <summary>帧外解析必抛（CurrentAopUser 空，守卫工厂拒绝）。</summary>
    public static void AssertResolvesOutsideScopeThrows<TInterface>(ServiceProvider provider)
        where TInterface : class
    {
        try
        {
            provider.GetRequiredService<TInterface>();
        }
        catch (InvalidOperationException)
        {
            return; // 预期：守卫工厂 CurrentAopUser 空抛 InvalidOperationException
        }
        throw new InvalidOperationException(
            $"[AssertConstructible] 帧外 GetRequiredService<{typeof(TInterface).Name}>() 未抛（域作用域守卫被绕过）");
    }
}