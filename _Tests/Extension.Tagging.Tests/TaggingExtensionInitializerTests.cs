using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Enumerations;
using TKWF.Ext.Tagging;
using TKWF.Ext.Tagging.Matchers;
using TKWF.Ext.Tagging.Processors;

namespace TKWF.Ext.Tagging.Tests;

/// <summary>
/// V4.9.79（D17 模式 3）：Tagging 扩展初始化器 + ITagService 功能测试。
/// <para>原 CfgStrongContractTests 的 TagService 用例随扩展迁出——cfg 建造器（UseTagService）已从
/// DomainOptions 移除（cfg 腱剥离），Tagging 经 [TKWFExtension] 自动发现注册。</para>
/// </summary>
public class TaggingExtensionInitializerTests
{
    private sealed class TestUserInfo : IUserInfo
    {
        public string UserIdString { get; set; } = "1";
        public string UserName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? SessionKey { get; set; }
        public List<string>? Roles { get; set; } = new();
        public TKW.Framework.Enumerations.EnumLoginFrom LoginFrom { get; set; }
    }

    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(TaggingExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Tagging", attr.Name);
        Assert.Equal("1.0.0", attr.Version);
    }

    [Fact]
    public void ConfigureServices_Registers_EngineAndFacade()
    {
        var services = new ServiceCollection();
        var init = new TaggingExtensionInitializer<TestUserInfo>();
        init.ConfigureServices(services);

        var sp = services.BuildServiceProvider();

        // 引擎流水线
        Assert.NotNull(sp.GetService<TagExtractionPipeline>());
        // 业务门面 ITagService（实现类 TagService）
        var tagService = sp.GetService<ITagService>();
        Assert.NotNull(tagService);
        Assert.IsType<TagService>(tagService);
        // 内置匹配器家族（5 个）
        var matchers = sp.GetServices<ITagMatcher>().ToList();
        Assert.Equal(5, matchers.Count);
        Assert.Contains(matchers, m => m is TokenExactMatcher);
        Assert.Contains(matchers, m => m is ContainsMatcher);
        Assert.Contains(matchers, m => m is RegexMatcher);
        Assert.Contains(matchers, m => m is StartsWithMatcher);
        Assert.Contains(matchers, m => m is EndsWithMatcher);
        // 后置处理器（2 个）
        var processors = sp.GetServices<ITagPipelinePostProcessor>().ToList();
        Assert.Contains(processors, p => p is ExclusionGroupProcessor);
        Assert.Contains(processors, p => p is DefaultTagProcessor);
    }

    [Fact]
    public void ITagService_GetTagsString_FormatsHits()
    {
        var services = new ServiceCollection();
        new TaggingExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        var sp = services.BuildServiceProvider();

        var tagService = sp.GetRequiredService<ITagService>();
        tagService.LoadRules([
            new TagRule { Dimension = "category", TagName = "电子", MatchMode = TagMatchMode.Contains, Pattern = "电子" },
            new TagRule { Dimension = "brand", TagName = "华为", MatchMode = TagMatchMode.Contains, Pattern = "华为" }
        ]);

        var result = tagService.GetTagsString("华为手机 电子产品");
        Assert.Contains("category:电子", result);
        Assert.Contains("brand:华为", result);
    }

    [Fact]
    public void ITagService_GetTags_ByDimension()
    {
        var services = new ServiceCollection();
        new TaggingExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        var sp = services.BuildServiceProvider();

        var tagService = sp.GetRequiredService<ITagService>();
        tagService.LoadRules([
            new TagRule { Dimension = "brand", TagName = "苹果", MatchMode = TagMatchMode.Contains, Pattern = "苹果" }
        ]);

        var hits = tagService.GetTagsForDimension("苹果手机", "brand");
        Assert.Single(hits);
        Assert.Equal("苹果", hits[0].TagName);

        var dims = tagService.GetDimensions();
        Assert.Contains("brand", dims);
    }
}
