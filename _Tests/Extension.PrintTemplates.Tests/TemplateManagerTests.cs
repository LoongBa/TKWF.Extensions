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
    /// <summary>创建 Manager（真实 Store + 真实 Renderer + VEntity 只读 DataService，默认 Options）。</summary>
    private static TemplateManager CreateManager(IFreeSql fsql)
        => TemplateTestSupport.CreateManager(fsql);

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

    // ── V0.2.0 VEntity 化（N3/N4/N5）：读路径单查询 JOIN 下推 DB（2 往返 → 1 往返）──

    /// <summary>
    /// N3：GetVersionAsync(key, version) 单查询返回视图实体（含 Key/TemplateName——当前返回面缺失的核心收益）。
    /// <para>预建模板（自定义 Name）→ 证明视图 JOIN 从 PrintTemplate 表取 Key/TemplateName，而非自动建模板的 Name=Key。</para>
    /// </summary>
    [Fact]
    public async Task GetVersionAsync_SingleQuery_ReturnsViewWithKeyAndTemplateName()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        var store = TemplateTestSupport.CreateStore(fsql);
        await store.UpsertTemplateAsync(new PrintTemplateEntity { Key = "Invoice.Standard", Name = "标准发票", Description = "tpl desc" });
        var tpl = await store.GetByKeyAsync("Invoice.Standard");
        await store.UpsertVersionAsync(new PrintTemplateVersionEntity
        {
            TemplateId = tpl!.Id,
            Version = "1.0.0",
            Content = "Hello {{ model.Name }}",
            Status = PrintTemplateVersionStatus.Active,
            Description = "ver desc"
        });

        var ver = await manager.GetVersionAsync("Invoice.Standard", "1.0.0");

        Assert.NotNull(ver);
        Assert.Equal("Invoice.Standard", ver!.Key);          // 核心收益：模板键（视图外层过滤键）
        Assert.Equal("标准发票", ver.TemplateName);           // 核心收益：模板名（旧实体返回面缺失）
        Assert.Equal("1.0.0", ver.Version);
        Assert.Equal(PrintTemplateVersionStatus.Active, ver.Status);
        Assert.Equal("Hello {{ model.Name }}", ver.Content);
        Assert.Equal("ver desc", ver.Description);
        Assert.Equal("tpl desc", ver.TemplateDescription);
    }

    /// <summary>
    /// N4：GetActiveVersionAsync(key) 返回 Active 版本（状态过滤下推 DB）。
    /// <para>同模板下 Archived + Active 并存 → 视图谓词 v.Status == Active 下推，仅返回 Active 行。</para>
    /// </summary>
    [Fact]
    public async Task GetActiveVersionAsync_SingleQuery_ReturnsActive()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);
        var manager = CreateManager(fsql);

        await manager.PublishAsync("k", "v1");
        await manager.PublishAsync("k", "v2"); // 1.0.0 → Archived，1.1.0 → Active

        var active = await manager.GetActiveVersionAsync("k");

        Assert.NotNull(active);
        Assert.Equal("1.1.0", active!.Version);
        Assert.Equal(PrintTemplateVersionStatus.Active, active.Status);
        Assert.Equal("k", active.Key);
    }

    /// <summary>
    /// N5：RenderAsync 渲染路径单查询（内部 GetActiveVersionAsync 走 vw_PrintTemplateVersionView，读取视图行 Content）。
    /// </summary>
    [Fact]
    public async Task RenderAsync_VEntityPath_RendersContentFromView()
    {
        using var fsql = TemplateTestSupport.CreateInMemoryFreeSql();
        TemplateTestSupport.SyncStructure(fsql);

        var store = TemplateTestSupport.CreateStore(fsql);
        await store.UpsertTemplateAsync(new PrintTemplateEntity { Key = "k", Name = "自定义模板名" });
        var tpl = await store.GetByKeyAsync("k");
        await store.UpsertVersionAsync(new PrintTemplateVersionEntity
        {
            TemplateId = tpl!.Id,
            Version = "1.0.0",
            Content = "{{ model.Name }} 你好",
            Status = PrintTemplateVersionStatus.Active
        });

        var manager = CreateManager(fsql);

        // JOIN 核心收益再确认（渲染取数同源：vw_PrintTemplateVersionView）
        var active = await manager.GetActiveVersionAsync("k");
        Assert.NotNull(active);
        Assert.Equal("自定义模板名", active!.TemplateName);

        var result = await manager.RenderAsync("k", RenderModel("Alice"));

        Assert.Equal("Alice 你好", result);
    }
}

