using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Permissions.Consumer.Tests;

/// <summary>
/// V0.8.0 (W4)：编译期权限名校验消费方冒烟——验证 PERM001 Analyzer 经
/// <c>ProjectReference + OutputItemType=Analyzer</c> 真实挂载到消费方编译。
/// <para>本文件故意含两类用法：
/// <list type="bullet">
/// <item><c>Order.Create</c>——已由 <see cref="ConsumerPermissionContributor"/> 声明 → 无 PERM001</item>
/// <item><c>Order.Mystery</c>——未声明 → 应产生 <c>PERM001</c> Warning（接线生效的证据）</item>
/// </list>
/// 构建本测试项目时，若输出含 PERM001 且 Order.Create 无误报，即证明 Analyzer 接线正确。
/// （该 Warning 是预期的负向验证，非缺陷；不影响测试运行。）</para>
/// </summary>
public interface IPermissionNameValidationSmoke
{
    [RequirePermission("Order.Create")]
    void DeclaredPermission();

    [RequirePermission("Order.Mystery")]
    void UndeclaredPermission();
}