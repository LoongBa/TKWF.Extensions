using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.PrintTemplates.Tests;

/// <summary>
/// 测试共享支撑（V0.3.0 领域自治根治重写——对齐 Settings 宿主生产路径范式）。
/// <list type="bullet">
/// <item><strong>生产路径集成</strong>：<see cref="CreateProductionHost"/>——真实 DI（扩展 ConfigureServices +
///     FreeSql 基础设施 + AddLogging）+ 真实 <see cref="DomainUser{TUserInfo}"/>（BindScope）→
///     <c>User.Use&lt;ITemplateStore&gt;()</c> / <c>User.Use&lt;ITemplateManager&gt;()</c> AOP 路径解析；</item>
/// <item><strong>分层单测</strong>：<see cref="StubDomainUser"/>——可配置用户桩，<c>Use&lt;T&gt;()</c> 按生产
///     NoAop 路径等价（ActivatorUtilities 直建，IDomainUser 参数显式传 this，其余从 DI 解析）；</item>
/// <item>VEntity：建真实视图 <c>vw_PrintTemplateVersionView</c>（SQLite 方言）——不跑宿主 SyncViewsAsync。</item>
/// </list>
/// <para>⚠️ 测试宿主补注册 <c>PrintTemplatesOptions</c> 裸类型（<c>AddOptions</c> 仅注册 IOptions——而
/// <see cref="ScribanTemplateRenderer"/> ctor 依赖裸 Options）——生产路径下 ITemplateRenderer 可解析的必要兜底
/// （见整改日志：记录待报框架组）。</para>
/// </summary>
internal static class TemplateTestSupport
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（每次调用新连接 = 独立内存库）。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
    {
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>同步两张表结构（模板 + 版本，含唯一索引）+ 建 VEntity 视图（SQLite 方言）。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<PrintTemplateEntity>();
        fsql.CodeFirst.SyncStructure<PrintTemplateVersionEntity>();
        // V0.2.0 VEntity：建真实视图（SQLite 方言，来自 PrintTemplateVersionView.ViewSqlSQLite）——不跑宿主 SyncViewsAsync。
        // 注意：SQLite 表无 PublishedAt/CreateTime 列（FreeSql SQLite provider 不支持 DateTimeOffset 建表，Identity 先例注释确认），
        // 视图用 NULL 占位对齐 C# 属性；PublishedBy 亦随审计列一并 NULL 占位——查询只需 Content/Status/Version/Key/TemplateName，日期列 NULL 不影响断言。
        fsql.Ado.ExecuteNonQuery(
            @"CREATE VIEW IF NOT EXISTS ""vw_PrintTemplateVersionView"" AS
SELECT v.""Id"", v.""TemplateId"", t.""Key"", t.""Name"" AS ""TemplateName"", t.""Description"" AS ""TemplateDescription"",
       v.""Version"", v.""Content"", v.""Status"", v.""Description"",
       NULL AS ""PublishedAt"", NULL AS ""PublishedBy"", NULL AS ""CreateTime""
FROM ""PrintTemplateVersion"" v
INNER JOIN ""PrintTemplate"" t ON v.""TemplateId"" = t.""Id""");
    }

    /// <summary>
    /// 生产路径 DI 主机（集成测试）：真实 DI（扩展 ConfigureServices + FreeSql 基础设施 + AddLogging）+
    /// <see cref="DomainUser{TUserInfo}"/> BindScope。
    /// </summary>
    public static (ServiceProvider Provider, DomainUser<TestUserInfo> User) CreateProductionHost()
    {
        var services = new ServiceCollection();
        // 1. 扩展装配——生产形态（消费方白名单启用后三钩子执行 ConfigureServices）
        new PrintTemplatesExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        // 2. 测试宿主基础设施：空 IConfiguration——AddOptions.BindConfiguration("TKWF:PrintTemplates")
        //    ConfigureNamedOptions 在解析 IOptions 时经 sp 取 IConfiguration（注册时取不到会延迟抛）
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // 3. 测试宿主兜底：ScribanTemplateRenderer ctor 依赖裸 PrintTemplatesOptions（AddOptions 不注册 T 本身）
        //    ——生产路径下 ITemplateRenderer（TryAddSingleton）可解析的必要条件（记录待报框架组）
        services.AddSingleton<PrintTemplatesOptions>(sp => sp.GetRequiredService<IOptions<PrintTemplatesOptions>>().Value);
        // 4. FreeSql 基础设施（消费方 DomainHost 等价注册）——实体 + VEntity 视图（IEntityReadOnlyDAC 红线契约）
        var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<PrintTemplateEntity>, FreeSqlEntityDAC<PrintTemplateEntity>>();
        services.AddSingleton<IEntityDAC<PrintTemplateVersionEntity>, FreeSqlEntityDAC<PrintTemplateVersionEntity>>();
        services.AddSingleton<IEntityReadOnlyDAC<PrintTemplateVersionView>, FreeSqlEntityDAC<PrintTemplateVersionView>>();
        // 5. 解析作用域绑定（生产经 DomainHost.NewDomainContext 绑定 AsyncLocal——测试等价 BindScope）
        var provider = services.BuildServiceProvider();
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };
        return (provider, user);
    }

    /// <summary>
    /// 分层单测宿主：构造 DataService 能力齐全的 ServiceProvider（stub 经 Use&lt;T&gt; 懒加载——
    /// 生产 NoAop 路径等价，ActivatorUtilities 直建 DataService/Store/Renderer，IEntityDAC 从 DI 解析）。
    /// </summary>
    public static StubDomainUser CreateStubUser(IFreeSql fsql)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<PrintTemplatesOptions>(new PrintTemplatesOptions());
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<PrintTemplateEntity>, FreeSqlEntityDAC<PrintTemplateEntity>>();
        services.AddSingleton<IEntityDAC<PrintTemplateVersionEntity>, FreeSqlEntityDAC<PrintTemplateVersionEntity>>();
        services.AddSingleton<IEntityReadOnlyDAC<PrintTemplateVersionView>, FreeSqlEntityDAC<PrintTemplateVersionView>>();
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
