using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Utility.Tags;
using TKWF.Ext.Tagging;

namespace TKWF.Ext.Tagging.Tests;

/// <summary>
/// V0.4.0：TaggingOptions 配置接入测试——InitializeAsync 消费（IServiceProviderAware 注入）。
/// <para>覆盖 Oracle P1-3 软校验 + 配置优先级（Store &gt; DefaultRules &gt; 无）。</para>
/// </summary>
public class TaggingOptionsTests
{
    private sealed class TestUserInfo : TKW.Framework.Domain.Interfaces.IUserInfo
    {
        public string UserIdString { get; set; } = "1";
        public string UserName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? SessionKey { get; set; }
        public List<string>? Roles { get; set; } = new();
        public TKW.Framework.Enumerations.EnumLoginFrom LoginFrom { get; set; }
    }

    /// <summary>测试 Store：可控规则源（验证 AutoLoadRulesFromStore 消费）。</summary>
    private sealed class StubTagRuleStore : ITagRuleStore
    {
        public List<TagRule> Rules { get; } = [];

        public Task<List<TagRule>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(Rules.ToList());
        public Task<List<TagRule>> GetEnabledAsync(CancellationToken ct = default) => Task.FromResult(Rules.Where(r => r.IsEnabled).ToList());
        public Task<long?> CreateAsync(TagRule rule, CancellationToken ct = default) => Task.FromResult<long?>(1);
        public Task<bool> UpdateAsync(TagRule rule, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> DeleteAsync(long id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<List<TagRule>> GetByDimensionAsync(string dimension, CancellationToken ct = default) => Task.FromResult(Rules.Where(r => r.Dimension == dimension).ToList());
    }

    private static (TaggingExtensionInitializer<TestUserInfo> init, ServiceProvider sp) Build(
        Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        // BindConfiguration 惰性读取 IConfiguration（OptionsBuilder.Configure）——测试需注册配置桩
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var init = new TaggingExtensionInitializer<TestUserInfo>();
        init.ConfigureServices(services);
        extra?.Invoke(services);
        var sp = services.BuildServiceProvider();
        init.ServiceProvider = sp;   // IServiceProviderAware 注入
        return (init, sp);
    }

    [Fact]
    public async Task Initialize_DefaultRules_LoadsIntoTagService()
    {
        var defaultRules = new[]
        {
            new TagRule { Dimension = "brand", TagName = "华为", MatchMode = TagMatchMode.DictMatch, Pattern = "华为" }
        };
        var (init, sp) = Build(s => s.Configure<TaggingOptions>(o => o.DefaultRules = defaultRules));

        await init.InitializeAsync();

        var tagService = sp.GetRequiredService<ITagService>();
        var hits = tagService.GetTags("华为手机");
        Assert.Single(hits);
        Assert.Equal("华为", hits[0].TagName);
    }

    [Fact]
    public async Task Initialize_AutoLoadRulesFromStore_LoadsEnabledRules()
    {
        var store = new StubTagRuleStore();
        store.Rules.Add(new TagRule { Dimension = "category", TagName = "电子", MatchMode = TagMatchMode.DictMatch, Pattern = "手机", IsEnabled = true });
        // 禁用规则 pattern 也出现在文本中——若误加载会 2 命中，验证 GetEnabledAsync 过滤真实性（Oracle P2-7）
        store.Rules.Add(new TagRule { Dimension = "hidden", TagName = "禁用", MatchMode = TagMatchMode.DictMatch, Pattern = "手机", IsEnabled = false });

        var (init, sp) = Build(s =>
        {
            s.AddSingleton<ITagRuleStore>(store);
            s.Configure<TaggingOptions>(o => o.AutoLoadRulesFromStore = true);
        });

        await init.InitializeAsync();

        var tagService = sp.GetRequiredService<ITagService>();
        var hits = tagService.GetTags("手机");
        Assert.Single(hits);                       // 仅启用规则（禁用规则若误加载会 2 命中）
        Assert.Equal("电子", hits[0].TagName);
    }

    [Fact]
    public async Task Initialize_StoreOverridesDefaultRules()
    {
        var store = new StubTagRuleStore();
        store.Rules.Add(new TagRule { Dimension = "store", TagName = "Store标签", MatchMode = TagMatchMode.DictMatch, Pattern = "苹果" });

        var (init, sp) = Build(s =>
        {
            s.AddSingleton<ITagRuleStore>(store);
            s.Configure<TaggingOptions>(o =>
            {
                o.AutoLoadRulesFromStore = true;
                o.DefaultRules = [new TagRule { Dimension = "cfg", TagName = "配置标签", MatchMode = TagMatchMode.DictMatch, Pattern = "苹果" }];
            });
        });

        await init.InitializeAsync();

        var tagService = sp.GetRequiredService<ITagService>();
        var hits = tagService.GetTags("苹果");
        Assert.Single(hits);                       // Store 覆盖配置
        Assert.Equal("Store标签", hits[0].TagName);
    }

    [Fact]
    public async Task Initialize_NoServiceProvider_SkipsSilently()
    {
        // Direct 场景（未注入 sp）→ 静默跳过（不抛异常）
        var init = new TaggingExtensionInitializer<TestUserInfo>();
        await init.InitializeAsync();              // ServiceProvider = null → return
    }

    [Fact]
    public async Task Initialize_NoConfig_WarningPath_NoCrash()
    {
        // 双路径均未配置 → 软校验 Warning（不阻断启动，不抛异常）
        var (init, sp) = Build();
        await init.InitializeAsync();

        var tagService = sp.GetRequiredService<ITagService>();
        Assert.Empty(tagService.GetTags("任何文本"));
    }

    [Fact]
    public void Options_ConfigSectionBinding_FromAppSettings()
    {
        // 模式 A 双通道：BindConfiguration 从配置节读取
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TKWF:Tagging:AutoLoadRulesFromStore"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddOptions<TaggingOptions>().BindConfiguration("TKWF:Tagging");
        var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<TaggingOptions>>().Value;
        Assert.True(options.AutoLoadRulesFromStore);
        Assert.Null(options.DefaultRules);         // 未配置 → null（无 [Required] 校验失败，P1-3）
    }
}
