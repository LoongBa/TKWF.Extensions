namespace TKWF.Ext.Permissions.Consumer.Tests;

/// <summary>
/// V0.7.0 (W4)：消费方权限贡献者——模拟真实消费方在业务模块声明自己的权限定义。
/// <para>经 <c>[PermissionContributor]</c> 标记，SG1 编译期扫描消费方程序集生成
/// <c>GeneratedPermissionContributors</c> → <see cref="TKW.Framework.CodeGeneration.ProjectMetaContextBase.PermissionContributors"/>，
/// 扩展 <see cref="PermissionExtensionInitializer{TUserInfo}.ConfigureServices"/> 运行时读取并实例化。</para>
/// </summary>
[PermissionContributor]
public class ConsumerPermissionContributor : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context)
    {
        context.Add(new PermissionDefinition
        {
            Name = "Order.Create",
            DisplayName = "创建订单",
            Group = "Order"
        });
        context.Add(new PermissionDefinition
        {
            Name = "Order.Delete",
            DisplayName = "删除订单",
            Group = "Order"
        });
    }
}
