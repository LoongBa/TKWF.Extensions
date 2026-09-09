using TKW.Framework.Domain;
using TKW.Framework.Utility.Tags;

namespace TKWF.Ext.Tagging;

/// <summary>
/// V0.4.0：标签扩展配置——经 <see cref="OptionsAttribute"/> 声明配置节，SG1 生成绑定 +
/// 结构校验（TKWF_OPT001/003/010）。
/// <para>配置节：<c>TKWF:Tagging</c>。消费方可在 appsettings.json 配置
/// （如 <c>"TKWF": { "Tagging": { "AutoLoadRulesFromStore": true, "DefaultRules": [...] } }</c>）；
/// 亦可在 <c>ConfigureExtensions</c> 中 <c>services.Configure&lt;TaggingOptions&gt;(o =&gt; ...)</c> 覆盖。</para>
/// <para>注：结构验证 ≠ 配置验证（D17 §4.11.3 诚实边界）——SG 验证声明完整与绑定生成，
/// 无法验证 appsettings.json 实际有值；缺省时走属性默认值。DefaultRules 不加 [Required]
/// （Oracle P1-3：Store-only 消费路径不配 DefaultRules 不应结构校验失败）。</para>
/// </summary>
[Options("TKWF:Tagging")]
public sealed class TaggingOptions
{
    /// <summary>
    /// 启动时自动从 <see cref="ITagRuleStore.GetEnabledAsync"/> 加载启用规则到 <see cref="ITagService"/>
    /// （闭环 v0.3.0 P2-3 规则陈旧性）。Store 规则覆盖配置节 <see cref="DefaultRules"/>（Store 为权威数据源）。
    /// 默认 false——消费方显式 <see cref="ITagService.LoadRules"/>。
    /// </summary>
    public bool AutoLoadRulesFromStore { get; set; }

    /// <summary>
    /// 配置节默认规则（静态场景——行业词库/基础标签，无需 DB）。可选（Oracle P1-3：不加 [Required]）。
    /// <c>TagRule</c> 直接作配置绑定目标——public setter + 枚举字符串绑定（如 <c>"MatchMode": "DictMatch"</c>）。
    /// 空/未配置 = 不注入（消费方显式 LoadRules 或启用 <see cref="AutoLoadRulesFromStore"/>）。
    /// </summary>
    public TagRule[]? DefaultRules { get; set; }
}
