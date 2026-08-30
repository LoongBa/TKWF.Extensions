using System.ComponentModel.DataAnnotations;
using TKW.Framework.Domain;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V4.9.76 (D1, W5)：权限扩展配置——经 <see cref="OptionsAttribute"/> 声明配置节，SG1 生成绑定 +
    /// 结构校验（TKWF_OPT001/003/010）。
    /// <para>配置节：<c>TKWF:Permissions</c>。消费方可在 appsettings.json 配置
    /// （如 <c>"TKWF": { "Permissions": { "DefaultPolicy": "RequireAdmin", "CacheTtlSeconds": 600 } }</c>）；
    /// 亦可在 <c>ConfigureExtensions</c> 中 <c>services.Configure&lt;PermissionOptions&gt;(o =&gt; ...)</c> 覆盖。</para>
    /// <para>注：结构验证 ≠ 配置验证（D17 §4.11.3 诚实边界）——SG 验证声明完整与绑定生成，
    /// 无法验证 appsettings.json 实际有值；缺省时走属性默认值。</para>
    /// </summary>
    [Options("TKWF:Permissions")]
    public sealed class PermissionOptions
    {
        /// <summary>默认权限策略（标记 [Required]——声明完整性的结构校验锚点）。</summary>
        [Required]
        public string DefaultPolicy { get; init; } = "Authenticated";

        /// <summary>权限缓存 TTL 秒数。</summary>
        public int CacheTtlSeconds { get; init; } = 300;
    }
}
