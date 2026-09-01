using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace TKWF.Ext.Permissions.Validation.Tests;

/// <summary>
/// PermissionNameValidatorAnalyzer 测试——覆盖 ADR 四个场景：
/// <list type="number">
/// <item>声明 + 引用一致 → 无 PERM001</item>
/// <item>引用未声明权限名 → PERM001</item>
/// <item>DLL 贡献者 → 跳过（不报错，运行时 fail-closed 兜底）</item>
/// <item>无贡献者 → PERM001</item>
/// </list>
/// </summary>
public class PermissionNameValidatorAnalyzerTests
{
    private const string Declared = """
        using TKWF.Ext.Permissions.Abstractions;

        namespace TestApp;

        [PermissionContributor]
        public class OrderPermissions : IPermissionDefinitionContributor
        {
            public void Define(PermissionDefinitionContext context)
            {
                context.Add(new PermissionDefinition { Name = "Order.Create", DisplayName = "创建订单" });
            }
        }
        """;

    private static ImmutableArray<Diagnostic> Run(string source)
        => AnalyzerTestHelper.RunAnalyzer(source, new PermissionNameValidatorAnalyzer(), PermissionNameValidatorAnalyzer.DiagnosticId);

    /// <summary>场景 1：源码贡献者声明 "Order.Create" + [RequirePermission("Order.Create")] → 无诊断。</summary>
    [Fact]
    public void DeclaredAndUsed_Match_NoDiagnostic()
    {
        var source = Declared + """
            public interface IOrderService
            {
                [RequirePermission("Order.Create")]
                void CreateOrder();
            }
            """;

        var diagnostics = Run(source);

        Assert.Empty(diagnostics);
    }

    /// <summary>场景 2：源码贡献者声明 "Order.Create" + [RequirePermission("Order.Delete")]（未声明）→ PERM001。</summary>
    [Fact]
    public void UsedButNotDeclared_ReportsPERM001()
    {
        var source = Declared + """
            public interface IOrderService
            {
                [RequirePermission("Order.Delete")]
                void DeleteOrder();
            }
            """;

        var diagnostics = Run(source);

        Assert.Single(diagnostics);
        Assert.Equal(PermissionNameValidatorAnalyzer.DiagnosticId, diagnostics[0].Id);
        Assert.Contains("Order.Delete", diagnostics[0].GetMessage());
    }

    /// <summary>场景 2b：多权限引用，其中一个未声明 → PERM001（只报未声明那个）。</summary>
    [Fact]
    public void MultiPermission_OneUndeclared_ReportsOnlyUndeclared()
    {
        var source = Declared + """
            public interface IOrderService
            {
                [RequirePermission("Order.Create", "Order.ForceDelete")]
                void DeleteOrder();
            }
            """;

        var diagnostics = Run(source);

        Assert.Single(diagnostics);
        Assert.Contains("Order.ForceDelete", diagnostics[0].GetMessage());
    }

    /// <summary>场景 3：引用程序集贡献者（DLL 贡献者）+ [RequirePermission] → 跳过（名字不可见，避免误报）。</summary>
    [Fact]
    public void DllContributor_SkipsValidation()
    {
        // 构造一个"引用程序集"（含贡献者的 DLL）→ 注入编译引用 → 主源码 [RequirePermission("Mystery.Perm")]
        // 该权限名仅在 DLL 贡献者中声明（对源码不可见）→ 不应误报
        var dllSource = """
            using TKWF.Ext.Permissions.Abstractions;

            namespace ContribLib;

            [PermissionContributor]
            public class HiddenPermissions : IPermissionDefinitionContributor
            {
                public void Define(PermissionDefinitionContext context)
                {
                    context.Add(new PermissionDefinition { Name = "Mystery.Perm" });
                }
            }
            """;

        var dllReference = AnalyzerTestHelper.CompileToReference(dllSource, "ContribLib");
        var mainSource = """
            public interface IAuditService
            {
                [RequirePermission("Mystery.Perm")]
                void Audit();
            }
            """;

        var diagnostics = AnalyzerTestHelper.RunAnalyzer(
            mainSource, new PermissionNameValidatorAnalyzer(), PermissionNameValidatorAnalyzer.DiagnosticId,
            extraReferences: new[] { dllReference });

        Assert.Empty(diagnostics); // DLL 贡献者存在 → 跳过，不报 PERM001
    }

    /// <summary>场景 4：无任何贡献者 + [RequirePermission("...")] → PERM001。</summary>
    [Fact]
    public void NoContributor_ReportsPERM001()
    {
        var source = """
            using TKWF.Ext.Permissions.Abstractions;

            namespace TestApp;

            public interface IOrderService
            {
                [RequirePermission("Order.Create")]
                void CreateOrder();
            }
            """;

        var diagnostics = Run(source);

        Assert.Single(diagnostics);
        Assert.Equal(PermissionNameValidatorAnalyzer.DiagnosticId, diagnostics[0].Id);
    }

    /// <summary>未引用 Permissions.Abstractions 时 Analyzer 静默跳过（无契约可校验）。</summary>
    [Fact]
    public void NoAbstractionsReference_SilentlySkips()
    {
        var source = """
            namespace TestApp;

            public interface IOrderService
            {
                void CreateOrder();
            }
            """;

        // 不含 Abstractions 引用的编译 → analyzer 不报任何 PERM001
        var diagnostics = AnalyzerTestHelper.RunAnalyzerWithoutAbstractions(
            source, new PermissionNameValidatorAnalyzer(), PermissionNameValidatorAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }
}