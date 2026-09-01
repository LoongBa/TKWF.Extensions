using System;

namespace TKWF.Ext.Permissions.Abstractions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W2/W5)：权限贡献者标记属性——SG1 据此识别并生成贡献者注册表。
    /// <para>标注在 <see cref="IPermissionDefinitionContributor"/> 实现类上（无属性载荷，纯标记）。</para>
    /// <para>V4.9.85 (ADR48 D7)：迁移至 Abstractions 项目（依赖倒置）。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PermissionContributorAttribute : Attribute
    {
    }
}
