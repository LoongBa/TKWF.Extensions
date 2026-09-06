using System;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.PrintTemplates.Tests;

/// <summary>
/// FreeSqlTemplateStore 测试——使用 SQLite 内存库验证真实 CRUD / 版本查询 / Active 查询 / 版本倒序列表。
/// <para>TemplateStore 经两个 DataService（SG1/xCodeGen 生成，FreeSqlEntityDAC + UnitOfWorkManager 驱动）委托持久化——
/// 数据访问红线整改后不直接注入 IFreeSql。</para>
/// <para>存储层异常传播（审计关键，不静默）：唯一约束冲突（同 TemplateId+Version 并发直插）显式抛异常——"并发发布败者"语义（C1）。</para>
/// </summary>
public class FreeSqlTemplateStoreTests
{
    [Fact]
    public async Task GetByKeyAsync_Exists_ReturnsTemplate()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var store = TemplateTestSupport.CreateStore(fsql);

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
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var store = TemplateTestSupport.CreateStore(fsql);

        var result = await store.GetByKeyAsync("No.Such.Key");

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertTemplateAsync_InsertsNewTemplate()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var store = TemplateTestSupport.CreateStore(fsql);

        await store.UpsertTemplateAsync(new PrintTemplateEntity { Key = "k1", Name = "n1" });

        var saved = await store.GetByKeyAsync("k1", CancellationToken.None);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task GetVersionAsync_Exists_ReturnsVersion()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var store = TemplateTestSupport.CreateStore(fsql);

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
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var store = TemplateTestSupport.CreateStore(fsql);

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
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var store = TemplateTestSupport.CreateStore(fsql);

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
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var store = TemplateTestSupport.CreateStore(fsql);

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

        var versions = await store.ListVersionsAsync(savedTemplate.Id, CancellationToken.None);
        Assert.Single(versions);
        Assert.Equal(savedTemplate.Id, versions[0].TemplateId);
        Assert.Equal("1.0.0", versions[0].Version);
        Assert.Equal("content", versions[0].Content);
    }

    // ── 异常传播（审计关键，不静默）——数据访问红线整改后补充 ──

    /// <summary>
    /// 唯一约束冲突（同 TemplateId+Version 并发直插）→ 数据库约束异常显式向上传播，不被吞掉（C1）。
    /// <para>模拟并发：绕过 Store 的"先查后插/更"预检，直接对已占用键再次 DataService Create——
    /// 等价于并发发布双方都过了预检后败者直插，命中 <c>IX_ptv_template_version</c> 唯一索引。</para>
    /// </summary>
    [Fact]
    public async Task UpsertVersionAsync_DuplicateTemplateVersion_ThrowsNotSwallowed()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var store = TemplateTestSupport.CreateStore(fsql);

        var template = new PrintTemplateEntity { Key = "k", Name = "n" };
        await store.UpsertTemplateAsync(template);
        var savedTemplate = await store.GetByKeyAsync("k");

        // 首次创建 1.0.0
        await store.UpsertVersionAsync(new PrintTemplateVersionEntity
        {
            TemplateId = savedTemplate!.Id,
            Version = "1.0.0",
            Content = "v1"
        });

        // 并发败者直插同键 → 唯一约束异常必须向上传播（不吞）
        var dupDataService = new PrintTemplateVersionEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<PrintTemplateVersionEntity>(new UnitOfWorkManager(fsql)));
        var ex = await Record.ExceptionAsync(() => dupDataService.EntityCreateAsync(new PrintTemplateVersionEntity
        {
            TemplateId = savedTemplate.Id,
            Version = "1.0.0",
            Content = "v2"
        }));

        // 异常显式暴露（审计关键资产，存储层错误不得静默）
        Assert.NotNull(ex);
        var versions = await store.ListVersionsAsync(savedTemplate.Id, CancellationToken.None);
        Assert.Single(versions);
    }
}