using System.Collections.Generic;
using System.Threading.Tasks;
using FreeSql;

namespace TKWF.Ext.PrintTemplates.Tests;

/// <summary>
/// TemplateManager 测试——版本生命周期（Publish/Draft/Archive）+ 渲染入口 + 发布版本自动递增。
/// <para>集成测试：真实 TemplateStore（经 SG1/xCodeGen DataService 委托，SQLite 内存）+ 真实 ScribanTemplateRenderer。</para>
/// </summary>
public class TemplateManagerTests
{
    /// <summary>创建 Manager（真实 Store + 真实 Renderer，默认 Options）。</summary>
    private static TemplateManager CreateManager(IFreeSql fsql)
    {
        var store = TemplateTestSupport.CreateStore(fsql);
        var renderer = new ScribanTemplateRenderer(new PrintTemplatesOptions());
        return new TemplateManager(store, renderer);
    }

    private static Dictionary<string, object?> RenderModel(string name = "Alice")
        => new()
        {
            ["model"] = new Dictionary<string, object?> { ["Name"] = name }
        };

    [Fact]
    public async Task PublishAsync_FirstVersion_Returns1_0_0()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        var result = await manager.PublishAsync("Invoice.Standard", "Hello {{ model.Name }}");

        Assert.Equal("1.0.0", result.Version);
        Assert.Equal(PrintTemplateVersionStatus.Active, result.Status);
        // 自动创建模板后版本应关联到该模板的 Id（非 0）
        Assert.True(result.TemplateId > 0, "发布后版本的 TemplateId 应回填自动创建模板的 Id");
    }

    [Fact]
    public async Task PublishAsync_SecondVersion_Returns1_1_0()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        await manager.PublishAsync("k", "v1");
        var result = await manager.PublishAsync("k", "v2");

        Assert.Equal("1.1.0", result.Version);
        Assert.Equal(PrintTemplateVersionStatus.Active, result.Status);
    }

    [Fact]
    public async Task PublishAsync_OldActive_Archived()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        await manager.PublishAsync("k", "v1");
        await manager.PublishAsync("k", "v2");

        var old = await manager.GetVersionAsync("k", "1.0.0");
        Assert.NotNull(old);
        Assert.Equal(PrintTemplateVersionStatus.Archived, old!.Status);

        var active = await manager.GetActiveVersionAsync("k");
        Assert.NotNull(active);
        Assert.Equal("1.1.0", active!.Version);
    }

    [Fact]
    public async Task PublishAsync_TemplateNotFound_AutoCreates()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        await manager.PublishAsync("Invoice.Standard", "content", "desc");

        // C2：Key 不存在时自动建 PrintTemplate 行，Name = Key
        var template = await manager.GetTemplateAsync("Invoice.Standard");
        Assert.NotNull(template);
        Assert.Equal("Invoice.Standard", template!.Key);
        Assert.Equal("Invoice.Standard", template.Name);
        Assert.Equal("desc", template.Description);
    }

    [Fact]
    public async Task DraftAsync_CreatesDraft()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        var result = await manager.DraftAsync("k", "draft content");

        Assert.Equal("1.0.0-draft", result.Version);
        Assert.Equal(PrintTemplateVersionStatus.Draft, result.Status);
        Assert.Equal("draft content", result.Content);
    }

    [Fact]
    public async Task DraftAsync_UpsertsExistingDraft()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        await manager.DraftAsync("k", "first draft");
        var result = await manager.DraftAsync("k", "updated draft");

        // 单 Draft：upsert 而非新增
        var all = await manager.ListVersionsAsync("k", CancellationToken.None);
        Assert.Equal(1, all.Count);
        Assert.Equal("updated draft", result.Content);

        Assert.Single(all);
        Assert.Equal("updated draft", all[0].Content);
    }

    [Fact]
    public async Task DraftAsync_DoesNotOccupyActive()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        await manager.DraftAsync("k", "draft content");

        // Draft 不占 Active 槽位
        var active = await manager.GetActiveVersionAsync("k");
        Assert.Null(active);
    }

    [Fact]
    public async Task ArchiveAsync_ArchivesVersion()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        await manager.PublishAsync("k", "content");
        await manager.ArchiveAsync("k", "1.0.0");

        var ver = await manager.GetVersionAsync("k", "1.0.0");
        Assert.NotNull(ver);
        Assert.Equal(PrintTemplateVersionStatus.Archived, ver!.Status);
    }

    [Fact]
    public async Task RenderAsync_NullVersion_RendersActive()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        await manager.PublishAsync("k", "Hello {{ model.Name }}");

        var result = await manager.RenderAsync("k", RenderModel("Alice"));

        Assert.Equal("Hello Alice", result);
    }

    [Fact]
    public async Task RenderAsync_SpecificVersion_RendersFixed()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        await manager.PublishAsync("k", "v1-{{ model.Name }}");
        await manager.PublishAsync("k", "v2-{{ model.Name }}");

        // 固定版本渲染（审计用）——即使 Active 已是 1.1.0，仍渲染 1.0.0
        var result = await manager.RenderAsync("k", RenderModel("Alice"), "1.0.0");

        Assert.Equal("v1-Alice", result);
    }
}

