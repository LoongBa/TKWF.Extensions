using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// 测试公共设施——StubDomainUser + 基于 FreeSqlEntityDAC 的 DictionaryStore 工厂。
/// <para>数据访问红线整改（2026-09-07）：扩展 Store 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;(new UnitOfWorkManager(fsql))</c> 驱动（与 Settings/BlobStoring 测试同模式）。</para>
/// <para>V0.2.0 VEntity：CreateStore 同步建真实视图 <c>vw_DictionaryItemView</c>（SQLite 方言）并接线
/// <see cref="DictionaryItemViewDataService"/>——与 ApprovalTestSupport.SyncStructure 同模式（不跑宿主 SyncViewsAsync）。</para>
/// </summary>
internal static class DataDictionaryTestHost
{
    /// <summary>创建基于 SQLite 内存库的 DictionaryStore（DataService 委托）。</summary>
    public static DictionaryStore CreateStore(IFreeSql fsql)
    {
        CreateView(fsql);
        var defDac = new FreeSqlEntityDAC<DictionaryDefinitionEntity>(new UnitOfWorkManager(fsql));
        var itemDac = new FreeSqlEntityDAC<DictionaryItemEntity>(new UnitOfWorkManager(fsql));
        var viewDac = new FreeSqlEntityDAC<DictionaryItemView>(new UnitOfWorkManager(fsql));
        var defDataService = new DictionaryDefinitionEntityDataService(new StubDomainUser(), defDac);
        var itemDataService = new DictionaryItemEntityDataService(new StubDomainUser(), itemDac);
        var viewDataService = new DictionaryItemViewDataService(new StubDomainUser(), viewDac);
        return new DictionaryStore(defDataService, itemDataService, viewDataService, NullLogger<DictionaryStore>.Instance);
    }

    /// <summary>创建真实视图 vw_DictionaryItemView（SQLite 方言，来自 DictionaryItemView.ViewSqlSQLite）。</summary>
    public static void CreateView(IFreeSql fsql)
    {
        fsql.Ado.ExecuteNonQuery(
            @"CREATE VIEW IF NOT EXISTS ""vw_DictionaryItemView"" AS
SELECT i.""Id"", i.""DefinitionId"", d.""Code"" AS ""DefinitionCode"", i.""Code"", i.""DisplayName"", i.""Value"",
       i.""Order"", i.""IsEnabled"", i.""ParentCode"", i.""Level"", i.""Path"", d.""DisplayName"" AS ""DefinitionDisplayName""
FROM ""DictionaryItem"" i
INNER JOIN ""DictionaryDefinition"" d ON i.""DefinitionId"" = d.""Id""");
    }
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户）。</summary>
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