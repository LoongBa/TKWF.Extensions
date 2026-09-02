using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
/// <item><see cref="ConfigureServices"/>——DI 构建前：注册来自 Utility 的分词器 + 匹配器家族 + 后置处理器 + 流水线 + 业务门面 <see cref="ITagService"/></item>
/// <item><see cref="ConfigureFilters"/>——Tagging 无全局过滤器（空实现）</item>
/// <item><see cref="InitializeAsync"/>——系统就绪后：空实现（规则加载由消费方调用 <see cref="ITagService.LoadRules"/> 或后续迭代 Options 注入）</item>
/// </list>
/// <para>命名空间 <c>TKWF.Ext.Tagging</c>（D17 §5.1 设计 + 包名约定 §4.6；算法类型见 Utility）。</para>
/// </summary>
[TKWFExtension("Tagging")]
public class TaggingExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称（对齐 [TKWFExtension] Name）。</summary>
    public override string Name => "Tagging";

    /// <summary>扩展描述。</summary>
    public override string Description => "标签存储扩展——标签算法来自 TKWF.Utility.Tags，持久化 V0.3.0 实施";

    public override void ConfigureServices(IServiceCollection services)
    {
        // 0. 默认分词器（TagExtractionPipeline 依赖；TryAdd* 幂等——消费方可自定义 ITokenizer 覆盖）
        services.TryAddSingleton<ITokenizer, DefaultTokenizer>();
        // 1. 内置基础匹配器家族（TryAddEnumerable 幂等）
        services.TryAddEnumerable([
            ServiceDescriptor.Singleton<ITagMatcher, TokenExactMatcher>(),
            ServiceDescriptor.Singleton<ITagMatcher, ContainsMatcher>(),
            ServiceDescriptor.Singleton<ITagMatcher, RegexMatcher>(),
            ServiceDescriptor.Singleton<ITagMatcher, StartsWithMatcher>(),
            ServiceDescriptor.Singleton<ITagMatcher, EndsWithMatcher>()
        ]);
        // 2. 默认后置处理器
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITagPipelinePostProcessor, ExclusionGroupProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ITagPipelinePostProcessor, DefaultTagProcessor>());
        // 3. 引擎流水线
        services.TryAddSingleton<TagExtractionPipeline>();
        // 4. 业务门面 ITagService（消费方经 Use<ITagService>() 调用；实现类 TagService 可从 DI 解析）
        services.TryAddSingleton<ITagService, TagService>();
        // 5. 存储扩展占位（V0.3.0 实施）：ITagRuleStore / ITagHitStore / ITagAnalysisService 后续注册
    }

    /// <summary>Tagging 无全局过滤器。</summary>
    public override void ConfigureFilters(FilterBuilder<TUserInfo> builder) { /* Tagging 无全局过滤器 */ }

    /// <summary>系统就绪后初始化（空实现——规则加载经 LoadRules 或后续迭代 Options 注入）。</summary>
    public override Task InitializeAsync() => Task.CompletedTask;
}