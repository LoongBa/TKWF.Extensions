using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="IMaterialService"/>（MaterialService 门面）测试——素材库（设计文档 §3.3.1）：
/// SaveMaterialAsync（Text/News 校验）/ ListMaterialsAsync（kind 过滤）/ DeleteMaterialAsync + <b>删除保护</b>（Oracle P1——
/// 素材被回复规则引用时禁止删除，防规则悬空）。
/// <para>生产路径：<c>user.Use&lt;IMaterialService&gt;()</c> AOP（AddConstructibleService 守卫工厂）。</para>
/// </summary>
public class MaterialServiceTests : WeChatOperationsTestHost
{
    /// <summary>SaveMaterialAsync Text 素材 → 创建成功 + ListMaterialsAsync 按 kind 过滤。</summary>
    [Fact]
    public async Task SaveAndList_TextMaterial()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IMaterialService>();

        var id = await service.SaveMaterialAsync(new MaterialEntity
        {
            Name = "欢迎文本",
            Kind = 1,
            Content = "欢迎语内容",
            IsEnabled = true,
        }, TestContext.Current.CancellationToken);

        Assert.True(id > 0);
        var list = await service.ListMaterialsAsync(kind: 1, TestContext.Current.CancellationToken);
        Assert.Single(list);
        Assert.Equal("欢迎文本", list[0].Name);
        Assert.Equal("欢迎语内容", list[0].Content);
    }

    /// <summary>SaveMaterialAsync News 素材校验（缺 Title/Url → ArgumentException）。</summary>
    [Fact]
    public async Task SaveAsync_News_RequiresTitleUrl()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IMaterialService>();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SaveMaterialAsync(new MaterialEntity
        {
            Name = "坏图文",
            Kind = 2,
            Title = "只有标题",
        }, TestContext.Current.CancellationToken));
        Assert.Contains("Title/Url", ex.Message);
    }

    /// <summary>DeleteMaterialAsync 未被规则引用 → 删除成功。</summary>
    [Fact]
    public async Task DeleteAsync_Unreferenced_Succeeds()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IMaterialService>();
        var id = await service.SaveMaterialAsync(new MaterialEntity
        {
            Name = "待删素材",
            Kind = 1,
            Content = "内容",
            IsEnabled = true,
        }, TestContext.Current.CancellationToken);

        var deleted = await service.DeleteMaterialAsync(id, TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.Empty(await service.ListMaterialsAsync(null, TestContext.Current.CancellationToken));
    }

    /// <summary>DeleteMaterialAsync 被规则引用 → 禁止删除（InvalidOperationException——防规则悬空，Oracle P1）。</summary>
    [Fact]
    public async Task DeleteAsync_ReferencedByRule_Throws()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IMaterialService>();
        var id = await service.SaveMaterialAsync(new MaterialEntity
        {
            Name = "被引用素材",
            Kind = 1,
            Content = "内容",
            IsEnabled = true,
        }, TestContext.Current.CancellationToken);

        // 规则引用该素材（Subscribe 场景）
        var ruleDs = user.Use<ReplyRuleEntityDataService>();
        await ruleDs.EntityCreateAsync(new ReplyRuleEntity
        {
            ChannelId = "ch-mat",
            SceneType = WeChatReplySceneTypes.Subscribe,
            MaterialId = id,
            IsEnabled = true,
            SortOrder = 1,
            CreateTime = System.DateTime.UtcNow,
            UpdateTime = System.DateTime.UtcNow,
        }, TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteMaterialAsync(id, TestContext.Current.CancellationToken));
        Assert.Contains("引用", ex.Message);
        Assert.Contains("禁止删除", ex.Message);
    }
}
