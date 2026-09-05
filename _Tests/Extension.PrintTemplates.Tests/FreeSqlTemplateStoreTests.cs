using System.Collections.Generic;
using System.Threading.Tasks;
using FreeSql;

namespace TKWF.Ext.PrintTemplates.Tests;

/// <summary>
/// FreeSqlTemplateStore 测试——使用 SQLite 内存库验证真实 CRUD / 版本查询 / Active 查询 / 版本倒序列表。
/// <para>存储层异常传播（审计关键，不静默）——异常路径经 Manager 层测试覆盖。</para>
/// </summary>
public class FreeSqlTemplateStoreTests
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（每次调用新连接 = 独立内存库）。</summary>
    private static IFreeSql CreateInMemoryFreeSql()
    {
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>同步两张表结构（模板 + 版本，含唯一索引）。</summary>
    private static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<PrintTemplateEntity>();
        fsql.CodeFirst.SyncStructure<PrintTemplateVersionEntity>();
    }

    [Fact]
    public async Task GetByKeyAsync_Exists_ReturnsTemplate()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var store = new FreeSqlTemplateStore(fsql);

        var template = new PrintTemplateEntity { Key = "Invoice.Standard", Name = "标准发票", Description = "测试模板" };
        await store.UpsertTemplateAsync(template);

        var result = await store.GetByKeyAsync("Invoice.Standard");

        Assert.NotNull(result);
        Assert.Equal("Invoice.Standard", result!.Key);
        Assert.Equal("标准发票", result.Name);
        Assert.Equal("测试模板", result.Description);
    }

    [Fact]
    public async Task GetByKeyAsync_NotExists_ReturnsNull()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var store = new FreeSqlTemplateStore(fsql);

        var result = await store.GetByKeyAsync("No.Such.Key");

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertTemplateAsync_InsertsNewTemplate()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var store = new FreeSqlTemplateStore(fsql);

        await store.UpsertTemplateAsync(new PrintTemplateEntity { Key = "k1", Name = "n1" });

        var count = fsql.Select<PrintTemplateEntity>().Count();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetVersionAsync_Exists_ReturnsVersion()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var store = new FreeSqlTemplateStore(fsql);

        var template = new PrintTemplateEntity { Key = "k", Name = "n" };
        await store.UpsertTemplateAsync(template);
        var savedTemplate = await store.GetByKeyAsync("k");

        var version = new PrintTemplateVersionEntity
        {
            TemplateId = savedTemplate!.Id,
            Version = "1.0.0",
            Content = "Hello {{ model.Name }}",
            Status = PrintTemplateVersionStatus.Active
        };
        await store.UpsertVersionAsync(version);

        var result = await store.GetVersionAsync(savedTemplate.Id, "1.0.0");

        Assert.NotNull(result);
        Assert.Equal("1.0.0", result!.Version);
        Assert.Equal("Hello {{ model.Name }}", result.Content);
        Assert.Equal(PrintTemplateVersionStatus.Active, result.Status);
    }

    [Fact]
    public async Task GetActiveVersionAsync_Exists_ReturnsActiveVersion()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var store = new FreeSqlTemplateStore(fsql);

        var template = new PrintTemplateEntity { Key = "k", Name = "n" };
        await store.UpsertTemplateAsync(template);
        var savedTemplate = await store.GetByKeyAsync("k");

        await store.UpsertVersionAsync(new PrintTemplateVersionEntity
        {
            TemplateId = savedTemplate!.Id,
            Version = "1.0.0",
            Content = "archived",
            Status = PrintTemplateVersionStatus.Archived
        });
        await store.UpsertVersionAsync(new PrintTemplateVersionEntity
        {
            TemplateId = savedTemplate.Id,
            Version = "1.1.0",
            Content = "active",
            Status = PrintTemplateVersionStatus.Active
        });

        var result = await store.GetActiveVersionAsync(savedTemplate.Id);

        Assert.NotNull(result);
        Assert.Equal("1.1.0", result!.Version);
        Assert.Equal("active", result.Content);
        Assert.Equal(PrintTemplateVersionStatus.Active, result.Status);
    }

    [Fact]
    public async Task ListVersionsAsync_ReturnsVersions()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var store = new FreeSqlTemplateStore(fsql);

        var template = new PrintTemplateEntity { Key = "k", Name = "n" };
        await store.UpsertTemplateAsync(template);
        var savedTemplate = await store.GetByKeyAsync("k");

        await store.UpsertVersionAsync(new PrintTemplateVersionEntity { TemplateId = savedTemplate!.Id, Version = "1.1.0", Content = "c1" });
        await store.UpsertVersionAsync(new PrintTemplateVersionEntity { TemplateId = savedTemplate.Id, Version = "1.0.0", Content = "c0" });
        await store.UpsertVersionAsync(new PrintTemplateVersionEntity { TemplateId = savedTemplate.Id, Version = "1.2.0", Content = "c2" });

        var result = await store.ListVersionsAsync(savedTemplate.Id);

        Assert.Equal(3, result.Count);
        // OrderByDescending(Version)：按版本号倒序
        Assert.Equal("1.2.0", result[0].Version);
        Assert.Equal("1.1.0", result[1].Version);
        Assert.Equal("1.0.0", result[2].Version);
    }

    [Fact]
    public async Task UpsertVersionAsync_InsertsNewVersion()
    {
        using var fsql = CreateInMemoryFreeSql();
        SyncStructure(fsql);
        var store = new FreeSqlTemplateStore(fsql);

        var template = new PrintTemplateEntity { Key = "k", Name = "n" };
        await store.UpsertTemplateAsync(template);
        var savedTemplate = await store.GetByKeyAsync("k");

        await store.UpsertVersionAsync(new PrintTemplateVersionEntity
        {
            TemplateId = savedTemplate!.Id,
            Version = "1.0.0",
            Content = "content",
            Status = PrintTemplateVersionStatus.Draft
        });

        var count = fsql.Select<PrintTemplateVersionEntity>().Count();
        Assert.Equal(1, count);

        var saved = fsql.Select<PrintTemplateVersionEntity>().First();
        Assert.Equal(savedTemplate.Id, saved.TemplateId);
        Assert.Equal("1.0.0", saved.Version);
        Assert.Equal("content", saved.Content);
    }
}
