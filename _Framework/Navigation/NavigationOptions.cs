using System.ComponentModel.DataAnnotations;
using TKW.Framework.Domain;

namespace TKWF.Ext.Navigation
{
    /// <summary>
    /// V4.9.76 (D1, W5)：导航扩展配置——经 <see cref="OptionsAttribute"/> 声明配置节，SG1 生成绑定 +
    /// 结构校验（TKWF_OPT001/003/010）。
    /// <para>配置节：<c>TKWF:Navigation</c>。消费方可在 appsettings.json 配置
    /// （如 <c>"TKWF": { "Navigation": { "DefaultMenuName": "Main", "MaxMenuDepth": 4 } }</c>）；
    /// 亦可在 <c>ConfigureExtensions</c> 中 <c>services.Configure&lt;NavigationOptions&gt;(o =&gt; ...)</c> 覆盖。</para>
    /// </summary>
    [Options("TKWF:Navigation")]
    public sealed class NavigationOptions
    {
        /// <summary>默认菜单名（[Required]——声明完整性校验锚点）。</summary>
        [Required]
        public string DefaultMenuName { get; init; } = "Main";

        /// <summary>菜单树最大深度（防深嵌套渲染性能问题）。</summary>
        public int MaxMenuDepth { get; init; } = 4;
    }
}