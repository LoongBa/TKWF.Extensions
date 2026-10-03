using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataPort.Tests;

/// <summary>
/// 测试公共设施——SQLite 内存库创建 + DataImportRecord 表结构同步。
/// <para>数据访问红线合规：DataImportTaskService 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;(new UnitOfWorkManager(fsql))</c> 驱动（与其他扩展测试宿主同模式）。</para>
/// </summary>
internal static class DataPortTestSupport
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（自动同步表结构）。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
        => new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();

    /// <summary>同步 DataImportRecord 表（含 FileHash/BatchNo 唯一索引）。</summary>
    public static void SyncStructure(IFreeSql fsql)
        => fsql.CodeFirst.SyncStructure<DataImportRecordEntity>();
}

/// <summary>
/// 完整测试宿主——V4.10.53（领域自治根治，ADR90，正确路线）重写走<b>生产路径</b>（对齐 Settings/FileManagement 宿主）：
/// <list type="bullet">
/// <item><b>真实 DI</b>（Initializer <c>ConfigureServices</c> + FreeSql 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c>；</item>
/// <item><b>门面经真实 <c>DomainUser&lt;TestUserInfo&gt;.Use&lt;IDataImportTaskService&gt;()</c> AOP 路径解析</b>
///     （框架 Use 内部设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂 → ActivatorUtilities 直建实现）——
///     <see cref="TaskService"/>（匿名——IDomainUser 永不注册 DI 语义保持，守卫工厂由 CurrentAopUser 传入，非 DI 解析）；</item>
/// <item><b>DataService 经 <c>User.Use&lt;DataImportRecordEntityDataService&gt;()</c> NoAop 路径直建</b>
///     （DataService 禁构造注入，DI004 零豁免；IEntityDAC 从 DI 解析——守卫工厂内 ctor 余参 ActivatorUtilities 解析）。</item>
/// <item><b>核心 ImportService（接线型框架类型）普通 DI 解析</b>：TryAddScoped&lt;IImportService&gt;（factory → ImportService）——
///     DataImportTaskService 守卫工厂 ctor 注入（框架类型零用户上下文，保持普通注册）；</item>
/// </list>
/// <para>每用例独立 <see cref="Create"/> 得到全新 SQLite 内存库（用例隔离）；<c>configure</c> 回调在扩展初始化器之后执行
/// ——可覆盖 IImportService（对齐 Calendar/OrganizationUnit/FeatureManagement 宿主模式）。</para>
/// </summary>
internal sealed class DataPortTestHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    /// <summary>生产 AOP 路径用户（真实 DomainUser&lt;TestUserInfo&gt;，匿名——经框架 Use 设 CurrentAopUser）。</summary>
    private readonly DomainUser<TestUserInfo> _domainUser = new();

    public IFreeSql Fsql { get; }

    /// <summary>门面——生产 AOP 路径：真实 DomainUser&lt;TestUserInfo&gt;.Use&lt;IDataImportTaskService&gt;()（匿名——守卫工厂直建实现）。</summary>
    public IDataImportTaskService TaskService => _domainUser.Use<IDataImportTaskService>();

    /// <summary>SG1 DataService（NoAop 路径直建——DataService 禁构造注入，DI004 零豁免；IEntityDAC 从 DI 解析）。</summary>
    public DataImportRecordEntityDataService DataService => _domainUser.Use<DataImportRecordEntityDataService>();

    private DataPortTestHost(ServiceProvider serviceProvider, IFreeSql fsql)
    {
        _serviceProvider = serviceProvider;
        Fsql = fsql;
    }

    /// <summary>全新宿主（每次调用独立 SQLite 内存库）。</summary>
    public static DataPortTestHost Create(
        DataPortOptions? options = null,
        Action<IServiceCollection>? configure = null)
    {
        var fsql = DataPortTestSupport.CreateInMemoryFreeSql();
        DataPortTestSupport.SyncStructure(fsql);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fsql);

        // DataImportRecordEntityDataService 的 IEntityDAC 基础设施注册（UoW 每解析独立——同生产 Host）
        services.AddScoped<IEntityDAC<DataImportRecordEntity>>(
            sp => new FreeSqlEntityDAC<DataImportRecordEntity>(new UnitOfWorkManager(sp.GetRequiredService<IFreeSql>())));

        // DataPort Options——先于初始化器注册（AddOptions 为 TryAdd 语义，不会覆盖测试实例；
        // 初始化器绑定的默认配置对已存在 IOptions 实例不生效，测试以显式 options 为准）
        services.AddSingleton<IOptions<DataPortOptions>>(Options.Create(options ?? new DataPortOptions()));

        // 扩展初始化器注册：核心 Import/Export + Provider（接线型 TryAddScoped）/ 任务门面（AddConstructibleService）
        new DataPortExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        // 生产路径：绑定当前异步流的解析作用域（DomainHost.NewDomainContext 等价——测试 BindScope）
        DomainUser<TestUserInfo>.BindScope(provider);
        return new DataPortTestHost(provider, fsql);
    }

    /// <summary>解析服务（Scoped 服务经根容器解析，生命周期与宿主一致）。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

    /// <summary>
    /// 释放宿主 + 解绑解析作用域。
    /// </summary>
    public void Dispose()
    {
        DomainUser<TestUserInfo>.UnBindScope();
        _serviceProvider.Dispose();
    }
}