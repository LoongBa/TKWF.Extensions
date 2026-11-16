using System;
using System.IO;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.RateLimitChecks;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.SmsCode.Tests;

/// <summary>
/// 短信验证码扩展测试宿主——生产路径（真实 DI + FreeSql SQLite 文件模式 + 真实 <see cref="DomainUser{TUserInfo}"/> BindScope）。
/// <para>对齐 WeChatChannelsTestHost/WeChatOperationsTestHost 宿主形态（非 Web 实体型扩展）——
/// 文件模式（DateTimeKind=Utc）规避 :memory: 连接池每连接独立内存库的跨 UoW 写读错乱。</para>
/// <para><b>M1 状态注记（⚠️[FACADE-M3] 标记）</b>：M1 落地范围 = 骨架 + 引擎 + 实体/DataService + Options + SPI 契约
/// （方案 §8 M1），<c>SmsCodeExtensionInitializer</c>（方案 §4.7）/ <c>SmsCodeService</c> 门面实现（方案 §4.3）
/// 属 M3 门面契约里程碑——尚未存在。宿主已按生产形态预接线全部周边设施（FreeSql 基础设施 + Options +
/// ScopeResolver 默认空串 + 频控 fallback），M3 落地后仅需补一行
/// <c>new SmsCodeExtensionInitializer&lt;TestUserInfo&gt;().ConfigureServices(services);</c>（见 ConfigureHost 标记处）。</para>
/// <para>DataService 经 <c>user.Use&lt;SmsCodeRecordEntityDataService&gt;()</c> NoAop 生产路径解析
/// （ActivateUtilities 直建，IEntityDAC 从 DI 解析——对齐 MFA 宿主 DataService 用法）。每用例独立宿主/独立库。</para>
/// </summary>
internal sealed class SmsCodeTestHost : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly DomainUser<TestUserInfo> _user;
    private readonly string? _dbPath;
    private bool _disposed;

    public IFreeSql Fsql { get; }

    /// <summary>短信验证码记录数据服务（生产 NoAop 路径——DataService ctor 经 ActivatorUtilities 直建，IEntityDAC 从 DI 解析）。</summary>
    public SmsCodeRecordEntityDataService Codes => _user.Use<SmsCodeRecordEntityDataService>();

    /// <summary>当前域用户（AOP 路径 CurrentAopUser 来源）。</summary>
    public DomainUser<TestUserInfo> User => _user;

    /// <summary>根 ServiceProvider（守卫形态断言——实现类直解抛 / 帧外解析抛，对齐 CaptchaTestHost）。</summary>
    public ServiceProvider Services => _provider;

    public T GetRequiredService<T>() where T : notnull => _provider.GetRequiredService<T>();

    private SmsCodeTestHost(ServiceProvider provider, DomainUser<TestUserInfo> user, IFreeSql fsql, string? dbPath)
    {
        _provider = provider;
        _user = user;
        Fsql = fsql;
        _dbPath = dbPath;
    }

    /// <summary>全新宿主（每用例独立 SQLite 文件库——用例隔离）。</summary>
    /// <param name="options">显式 Options（测试可用小频控阈值加速用例）；null = 默认值。</param>
    /// <param name="configure">额外注册/覆盖（在宿主基础设施之后执行）。</param>
    public static SmsCodeTestHost Create(SmsCodeOptions? options = null, Action<IServiceCollection>? configure = null)
    {
        var fsql = CreateFileFreeSql(out var dbPath);
        fsql.CodeFirst.SyncStructure<SmsCodeRecordEntity>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFreeSql>(fsql);
        ConfigureHost(services, options);
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();

        // 生产路径：绑定解析作用域 + 真实 DomainUser（AOP 路径 CurrentAopUser 来源）
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("smscode-test-user", "SMS 验证码测试用户") };

        return new SmsCodeTestHost(provider, user, fsql, dbPath);
    }

    /// <summary>SQLite 文件模式库（每次新建唯一临时文件——对齐 FileManagement/WeChatOperations CreateFileFreeSql 先例；
    /// DateTimeKind=Utc 统一 UTC 存储语义——方案 §4.1 ExpireAt/CreateTime UTC 契约）。</summary>
    private static IFreeSql CreateFileFreeSql(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), "tkfw-smscode-db-" + Guid.NewGuid().ToString("N") + ".db");
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={dbPath};DateTimeKind=Utc")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>宿主注册（生产等价面）。</summary>
    private static void ConfigureHost(IServiceCollection services, SmsCodeOptions? options)
    {
        // ② 扩展装配——生产路径（AddConstructibleService<ISmsCodeService, SmsCodeService> + Options 绑定 +
        //    ScopeResolver 默认 + IRateLimitCheck/MemoryCache fallback——方案 §4.7 m1）
        new SmsCodeExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // ③ FreeSql 基础设施（消费方 DomainHost 等价注册——共享单 UoW = 单连接单库，跨操作数据可见）
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<SmsCodeRecordEntity>>(
            sp => new FreeSqlEntityDAC<SmsCodeRecordEntity>(sp.GetRequiredService<UnitOfWorkManager>()));

        // ④ Options——先于初始化器注册（AddOptions 为 TryAdd 语义；测试以显式 options 为准，MFA 宿主同款）
        services.AddSingleton<IOptions<SmsCodeOptions>>(Options.Create(options ?? new SmsCodeOptions()));

        // ⑤ 作用域解析器默认空串（无租户消费方合法默认——方案 §4.5；正式由扩展初始器 TryAdd 注册，测试同语义）
        services.TryAddSingleton<ISmsCodeScopeResolver>(new EmptyScopeResolver());

        // ⑥ 频控 fallback（方案 §4.7 m1：TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>——消费方显式注册 DB 版时静默替换）
        services.TryAddSingleton<IRateLimitCheck, MemoryRateLimitCheck>();
    }

    /// <summary>默认空串作用域解析器（无租户消费方——键 <c>smscode::{scene}:{subject}</c>）。</summary>
    internal sealed class EmptyScopeResolver : ISmsCodeScopeResolver
    {
        public string? GetScopePrefix() => string.Empty;
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
        if (_dbPath is not null && File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
                // 库文件被占用（罕见）——忽略，系统 temp 清理兜底
            }
        }
    }
}