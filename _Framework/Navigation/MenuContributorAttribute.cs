using System;

namespace TKWF.Ext.Navigation
{
    /// <summary>
    /// V4.9.74 (扩展机制业务模块 W2/W5)：菜单贡献者标记属性——SG1 据此识别并生成贡献者注册表。
    /// <para>标注在 <see cref="IMenuContributor"/> 实现类上（无属性载荷，纯标记）。</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MenuContributorAttribute : Attribute
    {
    }
}