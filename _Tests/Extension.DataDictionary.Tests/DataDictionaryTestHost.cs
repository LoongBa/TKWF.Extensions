using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// 测试公共设施（V0.3.0 领域自治根治重写——对齐 Settings 宿主生产路径范式）。
/// <list type="bullet">
/// <item><strong>生产路径集成</strong>：<see cref="CreateProductionHost"/>——真实 DI（扩展 ConfigureServices +
///     FreeSql 基础设施 + AddLogging）+ 真实 <see cref="DomainUser{TUserInfo}"/>（BindScope）→
///     <c>User.Use&lt;IDictionaryStore&gt;()</c> / <c>User.Use&lt;IDictionaryManager&gt;()</c> AOP 路径解析；</item>
/// <item><strong>分层单测</strong>：<see cref="StubDomainUser"/>——可配置用户桩，<c>Use&lt;T&gt;()</c> 按生产
///     NoAop 路径等价（ActivatorUtilities 直建，IDomainUser 参数显式传 this，其余从 DI 解析）。</item>
/// <item>VEntity：建真实视图 <c>vw_DictionaryItemView</c>（SQLite 方言，来自 DictionaryItemView.ViewSqlSQLite）——
///     与 ApprovalTestSupport.SyncStructure 同模式（不跑宿主 SyncViewsAsync）。</item>
/// </list>
/// </summary>
internal static class DataDictionaryTestHost
{
    /// <summary>创建 SQLite 内存库 + 同步两张表结构 + 建 VEntity 视图（每次调用新连接 = 独立内存库）。</summary>
    public static IFreeSql CreateFreeSql()
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<DictionaryDefinitionEntity>();
        fsql.CodeFirst.SyncStructure<DictionaryItemEntity>();
        CreateView(fsql);
        return fsql;
    }

    /// <summary>创建真实视图 vw_DictionaryItemView（SQLite 方言，来自 DictionaryItemView.ViewSqlSQLite）。</summary>
    public static void CreateView(IFreeSql fsql)
    {
        fsql.Ado.ExecuteNonQuery(
            @"CREATE VIEW IF NOT EXISTS ""TKWFV_DictionaryItemView"" AS
SELECT i.""Id"", i.""DefinitionId"", d.""Code"" AS ""DefinitionCode"", i.""Code"", i.""DisplayName"", i.""Value"",
       i.""Order"", i.""IsEnabled"", i.""ParentCode"", i.""Level"", i.""Path"", d.""DisplayName"" AS ""DefinitionDisplayName""
FROM ""TKWF_DictionaryItem"" i
INNER JOIN ""TKWF_DictionaryDefinition"" d ON i.""DefinitionId"" = d.""Id""");
    }

    /// <summary>
    /// 生产路径 DI 主机（集成测试）：真实 DI（扩展 ConfigureServices + FreeSql 基础设施 + AddLogging）+
    /// <see cref="DomainUser{TUserInfo}"/> BindScope。可选配置回调用于按测试覆盖 <see cref="DataDictionaryOptions"/>。
    /// </summary>
    public static (ServiceProvider Provider, DomainUser<TestUserInfo> User) CreateProductionHost(
        Action<DataDictionaryOptions>? configure = null)
    {
        var services = new ServiceCollection();
        // 1. 扩展装配——生产形态（消费方白名单启用后三钩子执行 ConfigureServices）
        new DataDictionaryExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        if (configure != null)
            services.Configure<DataDictionaryOptions>(configure);
        // 2. FreeSql 基础设施（消费方 DomainHost 等价注册）——实体 + VEntity 视图（IEntityReadOnlyDAC 红线契约）
        var fsql = CreateFreeSql();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<DictionaryDefinitionEntity>, FreeSqlEntityDAC<DictionaryDefinitionEntity>>();
        services.AddSingleton<IEntityDAC<DictionaryItemEntity>, FreeSqlEntityDAC<DictionaryItemEntity>>();
        services.AddSingleton<IEntityReadOnlyDAC<DictionaryItemView>, FreeSqlEntityDAC<DictionaryItemView>>();
        // 3. 解析作用域绑定（生产经 DomainHost.NewDomainContext 绑定 AsyncLocal——测试等价 BindScope）
        var provider = services.BuildServiceProvider();
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };
        return (provider, user);
    }

    /// <summary>
    /// 分层单测宿主：构造 DataService 能力齐全的 ServiceProvider（stub 经 Use&lt;T&gt; 懒加载——
    /// 生产 NoAop 路径等价，ActivatorUtilities 直建 DataService，IEntityDAC 从 DI 解析）。
    /// </summary>
    public static StubDomainUser CreateStubUser(IFreeSql fsql)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<DictionaryDefinitionEntity>, FreeSqlEntityDAC<DictionaryDefinitionEntity>>();
        services.AddSingleton<IEntityDAC<DictionaryItemEntity>, FreeSqlEntityDAC<DictionaryItemEntity>>();
        services.AddSingleton<IEntityReadOnlyDAC<DictionaryItemView>, FreeSqlEntityDAC<DictionaryItemView>>();
        return new StubDomainUser { ServiceProvider = services.BuildServiceProvider() };
    }
}

/// <summary>
/// 测试用户桩（V0.3.0 重写）——实现 IDomainUser 最小契约；<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价：
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c>——IDomainUser 参数显式传 this，其余从 DI 解析
/// （对齐 Settings 宿主 StubDomainUser 范式）。
/// </summary>
internal sealed class StubDomainUser : IDomainUser
{
    private IServiceProvider? _provider;

    /// <summary>ServiceProvider（测试工厂注册时注入——Use&lt;T&gt; 解析源）。</summary>
    public IServiceProvider? ServiceProvider { set => _provider = value; get => _provider; }

    public string SessionKey => "test-session";
    public bool IsAuthenticated => false;
    public bool IsSystemActor => false;
    public IUserInfo? UserInfo => null;
    public long? TenantId => null;
    public bool IsNoAuditActive => false;
    public string? UserId => null;
    public string? UserName => null;
    public bool IsInRole(string role) => false;

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
    {
        // 生产 NoAop 路径等价：ActivatorUtilities 直建，IDomainUser 参数显式传 this，其余从 DI 解析
        if (_provider is null)
            throw new NotSupportedException("Stub: Use<T> 未注入 ServiceProvider");
        return (TDomainService)ActivatorUtilities.CreateInstance(_provider, typeof(TDomainService), this);
    }

    public TService GetService<TService>() where TService : notnull
    {
        if (_provider is null)
            throw new NotSupportedException("Stub: GetService<T> 未注入 ServiceProvider");
        return _provider.GetRequiredService<TService>();
    }

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
