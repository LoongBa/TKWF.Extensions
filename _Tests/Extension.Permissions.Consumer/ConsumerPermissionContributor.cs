using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Permissions.Consumer.Tests;

/// <summary>
/// V0.7.0 (W4)：消费方权限贡献者——模拟真实消费方在业务模块声明自己的权限定义。
/// <para>V4.10.31 (A+ 阶段 3)：删 <c>[PermissionContributor]</c> 特性——实现 <c>IPermissionDefinitionContributor</c>
/// 即被 SG1 接口判定自动发现（统一贡献者清单 <c>Contributors["Permission"]</c>），
/// 扩展 <see cref="PermissionExtensionInitializer{TUserInfo}.ConfigureServices"/> 运行时读取并实例化。</para>
/// </summary>
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
