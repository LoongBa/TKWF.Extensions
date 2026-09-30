using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.PrintTemplates.Tests;

/// <summary>
/// 测试共享支撑——SQLite 内存库 + DataService 构造 + StubDomainUser。
/// <para>TemplateStore 经两个 DataService（FreeSqlEntityDAC + UnitOfWorkManager 驱动）委托持久化，
/// 对齐 Settings/BlobStoring 测试构造（数据访问红线整改后 Store 不接收 IFreeSql）。</para>
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

    /// <summary>构造 TemplateStore——经真实 FreeSql DAC（UnitOfWorkManager + FreeSqlEntityDAC）驱动两个 DataService。</summary>
    public static TemplateStore CreateStore(IFreeSql fsql)
        => new(
            new PrintTemplateEntityDataService(
                new StubDomainUser(), new FreeSqlEntityDAC<PrintTemplateEntity>(new UnitOfWorkManager(fsql))),
            new PrintTemplateVersionEntityDataService(
                new StubDomainUser(), new FreeSqlEntityDAC<PrintTemplateVersionEntity>(new UnitOfWorkManager(fsql))));

    /// <summary>构造 VEntity 只读 DataService——经真实 FreeSql DAC 驱动（红线：IEntityReadOnlyDAC，绝不用 IEntityDAC）。</summary>
    public static PrintTemplateVersionViewDataService CreateViewDataService(IFreeSql fsql)
        => new(
            new StubDomainUser(), new FreeSqlEntityDAC<PrintTemplateVersionView>(new UnitOfWorkManager(fsql)));

    /// <summary>构造 TemplateManager（真实 Store + 真实 Renderer + VEntity 只读 DataService，默认 Options）。</summary>
    public static TemplateManager CreateManager(IFreeSql fsql)
        => new(CreateStore(fsql), new ScribanTemplateRenderer(new PrintTemplatesOptions()), CreateViewDataService(fsql));
}

/// <summary>最小 IDomainUser 桩——仅满足编译，不提供真实用户上下文。</summary>
internal sealed class StubDomainUser : IDomainUser
{
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
        => throw new NotSupportedException("Stub: Use<T> not supported in unit tests");
    public TService GetService<TService>() where TService : notnull
        => throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");
    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}