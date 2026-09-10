# TKWF.Ext.Permissions.Validation 编译期权限名校验分析器

**状态**: Roslyn DiagnosticAnalyzer（PERM001, V0.8.0） | **框架**: netstandard2.0 | **依赖**: Microsoft.CodeAnalysis.CSharp（PrivateAssets=all，不进入传递引用）

## 定位

Permissions 扩展的编译期权限名校验组件——把"未知权限名"从**运行时 fail-closed** 提前为**编译期 Warning**（IDE 即时反馈 + CI 警告）。扩展侧实现（ADR-Permissions-编译期权限名校验），内核对扩展契约零感知，不引入框架 SG 依赖。

## 校验语义（PERM001）

1. 扫描源码中 `[PermissionContributor]` 类的 `Define()` 方法体，提取 `context.Add(new PermissionDefinition { Name = "..." })` 的权限名字面量；
2. 收集全部 `[RequirePermission]` 特性参数字符串（含位置）；
3. 交叉比对——`[RequirePermission]` 引用了**未在贡献者中声明**的权限名 → **PERM001 Warning**（`Severity = Warning`，可升级为 Error）。

**边界（ADR D2）**：引用了 DLL 贡献者的项目（编译期程序集中含 `[PermissionContributor]` 类型，其 `Define()` 方法体无 SyntaxTree）→ **跳过整项校验**，由运行时 fail-closed 兜底（避免误报）；DLL 的 `[RequirePermission]` 参数仍经源码侧语法收集。未引用 `TKWF.Ext.Permissions.Abstractions` 的项目不触发（无契约可校验）。

## 接入方式（消费方）

```xml
<!-- 消费方 .csproj：ProjectReference + OutputItemType=Analyzer 一行接线
     （DiagnosticAnalyzer 非生成器，此方式可靠执行；仅 SG 生成器才需 build/refs 预编译） -->
<ItemGroup>
  <ProjectReference Include="..\..\_Framework\Permissions.Validation\TKWF.Ext.Permissions.Validation.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

## 触发示例与修复

```csharp
// ✗ PERM001：Order.Delete 未在贡献者 Define() 中声明（运行时权限检查将 fail-closed 拒绝）
public interface IOrderService
{
    [RequirePermission("Order.Delete")]
    Task DeleteAsync(int id);
}

// ✓ 修复：在 [PermissionContributor] 中声明该权限名，PERM001 消除
[PermissionContributor]
public class OrderPermissions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context)
        => context.Add(new PermissionDefinition { Name = "Order.Delete", DisplayName = "删除订单" });
}
```

## 随父扩展发布

属 Permissions 扩展（V0.8.0）的编译期工具组件（`PermissionNameValidatorAnalyzer`，`TKWF.Ext.Permissions.Validation` 命名空间）——本组件 `IsPackable=false`，以源码 ProjectReference 接线，不单独发布。权限定义 / 检查完整用法见 **Permissions 扩展使用指南**（`docs/Permissions/权限扩展-使用指南.md`）。