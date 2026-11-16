using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Captcha.Tests;

/// <summary>
/// 人机验证扩展测试宿主——生产路径（真实 DI + 真实 <see cref="DomainUser{TUserInfo}"/> BindScope）。
/// <para>对齐 SmsCodeTestHost/MFA TestHost 宿主形态（纯内存型扩展——零实体零持久化零 FreeSql；
/// 挑战态 in-memory IMemoryCache 随宿主实例独立，每用例独立宿主 = 挑战态天然隔离）。</para>
/// <para>注册：<c>CaptchaExtensionInitializer.ConfigureServices</c>（生产装配——门面 AddConstructibleService +
/// Provider 集合 TryAddEnumerableConstructible + Options/IRateLimitCheck/IMemoryCache fallback）+ 显式
/// <c>Options.Create</c> 后注册胜出（SmsCode/MFA 宿主同款——测试以显式 options 为准，可产低 TTL/低频控阈值用例）。</para>
/// <para>门面经 <c>user.Use&lt;ICaptchaService&gt;()</c> AOP 生产路径解析（守卫工厂 + CurrentAopUser 供给）；
/// Provider/挑战态经 <c>user.Use&lt;ImageCaptchaProvider&gt;()</c> NoAop 路径直建（ActivatorUtilities 直建——
/// IDomainUser 参数由 NoAop 工厂注入当前 DomainUser，框架 CreateNoAop 语义）。每用例独立宿主。</para>
/// </summary>
internal sealed class CaptchaTestHost : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly DomainUser<TestUserInfo> _user;
    private bool _disposed;

    /// <summary>当前域用户（AOP 路径 CurrentAopUser 来源）。</summary>
    public DomainUser<TestUserInfo> User => _user;

    /// <summary>人机验证门面（生产 AOP 路径——AddConstructibleService 守卫工厂）。</summary>
    public ICaptchaService Captcha => _user.Use<ICaptchaService>();

    /// <summary>本地图形 Provider（NoAop 直建路径——Attempts/TTL 语义直接命中断言）。</summary>
    public ImageCaptchaProvider Provider => _user.Use<ImageCaptchaProvider>();

    /// <summary>根 ServiceProvider（守卫形态断言——实现类直解抛 / 帧外解析抛）。</summary>
    public ServiceProvider Services => _provider;

    public T GetRequiredService<T>() where T : notnull => _provider.GetRequiredService<T>();

    private CaptchaTestHost(ServiceProvider provider, DomainUser<TestUserInfo> user)
    {
        _provider = provider;
        _user = user;
    }

    /// <summary>全新宿主（每用例独立——挑战态 IMemoryCache 随宿主隔离）。</summary>
    /// <param name="options">显式 Options（测试可用小 TTL/小频控阈值加速用例）；null = 默认值。</param>
    /// <param name="configure">额外注册/覆盖（在宿主基础设施之后执行——如注入 IHttpContextAccessor 桩）。</param>
    public static CaptchaTestHost Create(CaptchaOptions? options = null, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        ConfigureHost(services, options);
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();

        // 生产路径：绑定解析作用域 + 真实 DomainUser（AOP 路径 CurrentAopUser 来源）
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("captcha-test-user", "CAPTCHA 测试用户") };

        return new CaptchaTestHost(provider, user);
    }

    /// <summary>宿主注册（生产等价面）。</summary>
    private static void ConfigureHost(IServiceCollection services, CaptchaOptions? options)
    {
        // ② 扩展装配——生产路径（AddConstructibleService 门面 + TryAddEnumerableConstructible Provider 集合 +
        //    Options + IRateLimitCheck/IMemoryCache/挑战态 Store fallback——方案 §5.6 m1）
        new CaptchaExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // ③ IHttpContextAccessor——门面 ctor 所需（生产由 ASP.NET Core 宿主自动注册；测试宿主等价注册）。
        //    ⚠️ 注：CaptchaService ctor 声明 `IHttpContextAccessor?` 无 `= null` 默认值——ActivatorUtilities
        //    视其必选参数——裸 ServiceCollection 宿主须显式注册，否则守卫工厂激活失败（"Unable to resolve service"）。
        //    测试默认 HttpContextAccessor（HttpContext=null → GetClientIp()=null → unknown 桶；单测场景等价）。
        //    configure 钩子里的桩化 accessor 后注册胜出（AddSingleton 覆盖）。
        services.TryAddSingleton<IHttpContextAccessor>(new HttpContextAccessor());

        // ④ Options——后注册胜出（AddOptions 兜底默认值；测试以显式 options 为准，SmsCode/MFA 宿主同款）
        services.AddSingleton<IOptions<CaptchaOptions>>(Options.Create(options ?? new CaptchaOptions()));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            DomainUser<TestUserInfo>.UnBindScope();
        }
        catch
        {
            // BindScope 未建立的宿主 Dispose 不抛——忽略 UnBindScope 失败
        }
        _provider.Dispose();
    }
}