using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.Tags;
using TKW.Framework.Utility.Tags.Matchers;
using TKW.Framework.Utility.Tags.Processors;

namespace TKWF.Ext.Tagging;

/// <summary>
/// ADR52 (V4.9.91)：标签存储扩展初始化器——标签算法已回归 <c>TKWF.Utility</c>（<c>TKW.Framework.Utility.Tags</c>），
/// 本扩展瘦身为<b>标签存储扩展</b>：DI 接线（注册 Utility 算法服务）+ 规则/命中持久化（V0.3.0 实施）。
/// 经 [TKWFExtension] 被 SG1 编译期发现，三钩子接线：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——DI 构建前：注册来自 Utility 的分词器 + 匹配器家族 + 批量匹配器（AC 自动机 V0.4.0）+ 后置处理器 + 流水线 + 业务门面 <see cref="ITagService"/> + Options（V0.4.0）</item>
/// <item><see cref="ConfigureFilters"/>——Tagging 无全局过滤器（空实现）</item>
/// <item><see cref="InitializeAsync"/>——系统就绪后：Options 消费（V0.4.0）——配置节默认规则 + Store 自动加载</item>
/// </list>
/// <para>命名空间 <c>TKWF.Ext.Tagging</c>（D17 §5.1 设计 + 包名约定 §4.6；算法类型见 Utility）。</para>
/// <para>V0.4.0：实现 <see cref="IServiceProviderAware"/>（V4.9.76 D2）——<see cref="InitializeAsync"/> 前注入
/// ServiceProvider，scope 内解析 Options + Store 消费（对齐 Permissions 先例）。</para>
/// </summary>
[TKWFExtension("Tagging")]
public class TaggingExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>, IServiceProviderAware
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称（对齐 [TKWFExtension] Name）。</summary>
    public override string Name => "Tagging";

    /// <summary>扩展描述。</summary>
    public override string Description => "标签存储扩展——标签算法来自 TKWF.Utility.Tags，持久化 V0.3.0 实施，AC 自动机 + Options V0.4.0 实施";

    /// <summary>注入的 IServiceProvider（V4.9.76 D2：InitializeAsync 前注入；测试 Direct 场景为 null）。</summary>
    public IServiceProvider? ServiceProvider { get; set; }

    public override void ConfigureServices(IServiceCollection services)
    {
        // 0. Options（V0.4.0 模式 A 双通道：显式 BindConfiguration + [Options] 特性 SG 自动绑定，对齐 SecurityLog 先例）
        services.AddOptions<TaggingOptions>().BindConfiguration("TKWF:Tagging");

        // 1. 默认分词器（TagExtractionPipeline 依赖；TryAdd* 幂等——消费方可自定义 ITokenizer 覆盖）
        services.TryAddSingleton<ITokenizer, DefaultTokenizer>();
        // 2. 内置基础匹配器家族（TryAddEnumerable 幂等）
        services.TryAddEnumerable([
            ServiceDescriptor.Singleton<ITagMatcher, TokenExactMatcher>(),
            ServiceDescriptor.Singleton<ITagMatcher, ContainsMatcher>(),
            ServiceDescriptor.Singleton<ITagMatcher, RegexMatcher>(),
            ServiceDescriptor.Singleton<ITagMatcher, StartsWithMatcher>(),
            ServiceDescriptor.Singleton<ITagMatcher, EndsWithMatcher>(),
            ServiceDescriptor.Singleton<ITagMatcher, FullMatchMatcher>()   // V0.4.0 P2-5：补齐 FullMatch 既存缺口
        ]);
        // 2.5 批量匹配器（V0.4.0：AC 自动机 DictMatch——TryAddEnumerable 幂等，消费方可覆盖）
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITagBatchMatcher, AcAutomataBatchMatcher>());
        // 3. 默认后置处理器
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITagPipelinePostProcessor, ExclusionGroupProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITagPipelinePostProcessor, DefaultTagProcessor>());
        // 4. 引擎流水线
        services.TryAddSingleton<TagExtractionPipeline>();
        // 5. 业务门面 ITagService（消费方经 Use<ITagService>() 调用；实现类 TagService 可从 DI 解析）
        services.TryAddSingleton<ITagService, TagService>();
        // 6. V0.3.0 存储扩展（持久化）：规则/命中/分析——Store 委托 SG1 DataService（红线合规，TryAddScoped 消费方可覆盖）
        services.TryAddScoped<ITagRuleStore, FreeSqlTagRuleStore>();
        services.TryAddScoped<ITagHitStore, FreeSqlTagHitStore>();
        services.TryAddScoped<ITagAnalysisService, FreeSqlTagAnalysisService>();
    }

    /// <summary>Tagging 无全局过滤器。</summary>
    public override void ConfigureFilters(FilterBuilder<TUserInfo> builder) { /* Tagging 无全局过滤器 */ }

    /// <summary>
    /// 系统就绪后初始化（V0.4.0）：Options 消费——配置节默认规则 + Store 自动加载。
    /// 配置优先级：Store 规则 &gt; 配置节 DefaultRules &gt; 无（消费方显式 LoadRules）。
    /// </summary>
    public override async Task InitializeAsync()
    {
        if (ServiceProvider is null) return;                     // 未注入（测试 Direct 场景）静默跳过
        using var scope = ServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var options = sp.GetService<IOptions<TaggingOptions>>()?.Value;
        var tagService = sp.GetService<ITagService>();
        if (options is null || tagService is null) return;

        // 1. 配置节默认规则（静态场景）
        if (options.DefaultRules is { Length: > 0 })
            tagService.LoadRules(options.DefaultRules);

        // 2. Store 自动加载（动态场景，覆盖配置默认——Store 权威）
        if (options.AutoLoadRulesFromStore)
        {
            var store = sp.GetService<ITagRuleStore>();
            if (store is not null)
            {
                var rules = await store.GetEnabledAsync();
                if (rules.Count > 0) tagService.LoadRules(rules);
            }
        }

        // 3. 软校验（Oracle P1-3）：两条自动加载路径均未配置 → Warning 提示（非结构校验，不阻断启动）
        if (!options.AutoLoadRulesFromStore && options.DefaultRules is not { Length: > 0 })
            sp.GetService<ILogger<TaggingExtensionInitializer<TUserInfo>>>()
                ?.LogWarning("Tagging 未配置 DefaultRules 且未启用 AutoLoadRulesFromStore——规则需消费方显式 LoadRules。");
    }
}
