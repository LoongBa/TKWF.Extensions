using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TKWF.Ext.Permissions.Validation
{
    /// <summary>
    /// V0.8.0 (ADR-Permissions-编译期权限名校验)：编译期权限名校验分析器——PERM001。
    /// <para>将"未知权限名"从运行时 fail-closed 提前为编译期 Diagnostic 警告（IDE 即时反馈 + CI 警告）。</para>
    /// <para>机制：扫描源码实现 <c>IPermissionDefinitionContributor</c> 接口（v4.10.31 A+ 阶段 3 起接口判定，
    /// 不再用 <c>[PermissionContributor]</c> 特性）的 <c>Define()</c> 方法体，提取
    /// <c>context.Add(new PermissionDefinition { Name = "..." })</c> 字符串字面量；收集 <c>[RequirePermission]</c>
    /// 参数字符串；交叉比对——未声明的权限名 → PERM001 Warning。</para>
    /// <para>边界（ADR D2）：DLL 贡献者（引用程序集的 Define() 无 SyntaxTree，方法体不可见）→ 跳过整个校验，
    /// 由运行时 fail-closed 兜底（避免误报）。DLL <c>[RequirePermission]</c> 参数仍经语法收集（源码侧）。</para>
    /// <para>扩展侧实现（从内核移除）：本分析器不依赖内核 SG，零框架改动——符合"内核不得感知扩展契约"原则。</para>
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PermissionNameValidatorAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>
        /// 权限定义贡献者接口完整类型名（扩展 Abstractions）。
        /// V4.10.31 (A+ 阶段 3, P0-1)：由 PermissionContributorAttribute 特性改为 IPermissionDefinitionContributor 接口
        /// ——贡献者发现改接口判定（实现接口即贡献者），特性已删除（否则 PERM001 静默失效）。
        /// </summary>
        private const string PermissionContributorInterfaceFqn = "TKWF.Ext.Permissions.Abstractions.IPermissionDefinitionContributor";

        /// <summary>方法级权限声明特性完整类型名（扩展 Abstractions）。</summary>
        private const string RequirePermissionAttributeFqn = "TKWF.Ext.Permissions.Abstractions.RequirePermissionAttribute";

        /// <summary>权限定义类型名（用于匹配 object creation 的类型名）。</summary>
        private const string PermissionDefinitionTypeName = "PermissionDefinition";

        /// <summary>诊断 ID。</summary>
        public const string DiagnosticId = "PERM001";

        private static readonly DiagnosticDescriptor Rule = new(
            DiagnosticId,
            "未知权限名",
            "权限名 '{0}' 未在任何 IPermissionDefinitionContributor 的 Define() 中声明（运行时将 fail-closed）",
            "Permissions",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "编译期权限名校验：RequirePermission 引用了未声明的权限名（ADR D4，Warning 可升级为 Error）。");

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(startContext =>
            {
                var compilation = startContext.Compilation;
                var contributorInterface = compilation.GetTypeByMetadataName(PermissionContributorInterfaceFqn);
                var requirePermissionAttr = compilation.GetTypeByMetadataName(RequirePermissionAttributeFqn);
                if (contributorInterface is null || requirePermissionAttr is null)
                    return; // 未引用 Permissions.Abstractions——无契约可校验

                var state = new ValidationState();

                // 1. 符号动作：识别源码实现 IPermissionDefinitionContributor 的类型并提取 Define() 中声明的权限名
                startContext.RegisterSymbolAction(symbolContext =>
                {
                    if (symbolContext.Symbol is not INamedTypeSymbol type) return;
                    if (!ImplementsContributorInterface(type, contributorInterface)) return;
                    if (!type.Locations.Any(l => l.IsInSource)) return; // DLL 贡献者——单独显式扫描判定

                    // 源码贡献者：提取 Define() 中 Name = "..." 字面量
                    var defineMethod = type.GetMembers("Define").OfType<IMethodSymbol>().FirstOrDefault();
                    if (defineMethod?.DeclaringSyntaxReferences is not { Length: > 0 } refs) return;
                    if (refs[0].GetSyntax() is not MethodDeclarationSyntax defineSyntax) return;
                    foreach (var name in ExtractPermissionNames(defineSyntax))
                        state.DeclaredNames.TryAdd(name, 0);
                }, SymbolKind.NamedType);

                // 2. 语法动作：收集 [RequirePermission] 参数字符串（含位置）
                startContext.RegisterSyntaxNodeAction(nodeContext =>
                {
                    if (nodeContext.Node is not AttributeSyntax attr) return;
                    if (!IsRequirePermissionAttribute(attr, nodeContext.SemanticModel, requirePermissionAttr)) return;
                    foreach (var usage in ExtractAttributePermissionNames(attr))
                        state.RequireUsages.Add(usage);
                }, SyntaxKind.Attribute);

                // 3. 编译结束：DLL 贡献者显式扫描（引用程序集）+ 交叉校验 → PERM001
                startContext.RegisterCompilationEndAction(endContext =>
                {
                    // DLL 贡献者存在（引用程序集中 [PermissionContributor]）→ 名字不可见 → 跳过整个校验
                    // （ADR D2：DLL 贡献者 Define() 方法体无 SyntaxTree，避免误报，运行时 fail-closed 兜底）
                    if (HasReferencedContributor(compilation, contributorInterface)) return;

                    foreach (var (location, name) in state.RequireUsages)
                    {
                        if (!state.DeclaredNames.ContainsKey(name))
                            endContext.ReportDiagnostic(Diagnostic.Create(Rule, location, name));
                    }
                });
            });
        }

        /// <summary>
        /// 显式扫描引用程序集：是否存在实现 <c>IPermissionDefinitionContributor</c> 的类型（DLL 贡献者）。
        /// <para>仅扫描引用了 Permissions.Abstractions 的程序集（含该接口程序集）以控制开销。</para>
        /// </summary>
        private static bool HasReferencedContributor(Compilation compilation, INamedTypeSymbol contributorInterface)
        {
            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                // 快速筛选：只有引用 Abstractions（接口所在程序集）的 DLL 才可能含贡献者
                if (!ReferencesAssemblyNamed(assembly, "TKWF.Ext.Permissions.Abstractions")) continue;
                if (NamespaceHasContributor(assembly.GlobalNamespace, contributorInterface)) return true;
            }
            return false;
        }

        private static bool ReferencesAssemblyNamed(IAssemblySymbol assembly, string assemblyName)
        {
            foreach (var module in assembly.Modules)
                foreach (var referenced in module.ReferencedAssemblySymbols)
                    if (referenced.Name == assemblyName) return true;
            return false;
        }

        private static bool NamespaceHasContributor(INamespaceSymbol ns, INamedTypeSymbol contributorInterface)
        {
            foreach (var type in ns.GetTypeMembers())
                if (ImplementsContributorInterface(type, contributorInterface))
                    return true;

            foreach (var childNs in ns.GetNamespaceMembers())
                if (NamespaceHasContributor(childNs, contributorInterface))
                    return true;

            return false;
        }

        /// <summary>类型是否实现权限定义贡献者接口（含继承链——对齐 SG AllInterfaces 语义）。</summary>
        private static bool ImplementsContributorInterface(INamedTypeSymbol type, INamedTypeSymbol contributorInterface)
        {
            foreach (var iface in type.AllInterfaces)
                if (SymbolEqualityComparer.Default.Equals(iface, contributorInterface))
                    return true;
            return false;
        }

        /// <summary>编译内共享校验状态（跨符号/语法/结束动作）。
        /// <para>并发安全（V0.8.1 修复）：EnableConcurrentExecution 下符号/语法动作并发执行——
        /// DeclaredNames 用 ConcurrentDictionary（键集合），RequireUsages 用 ConcurrentBag。</para></summary>
        private sealed class ValidationState
        {
            /// <summary>源码贡献者已声明的权限名集合（ConcurrentDictionary 作并发 Set，V0.8.1）。</summary>
            public ConcurrentDictionary<string, byte> DeclaredNames { get; } = new(StringComparer.Ordinal);

            /// <summary>[RequirePermission] 使用位置 + 权限名（ConcurrentBag，V0.8.1）。</summary>
            public ConcurrentBag<(Location Location, string Name)> RequireUsages { get; } = new();
        }

        /// <summary>特性语法是否为 [RequirePermission]（按语义类型全等判定）。</summary>
        private static bool IsRequirePermissionAttribute(
            AttributeSyntax attr, SemanticModel semanticModel, INamedTypeSymbol requirePermissionAttr)
        {
            if (semanticModel.GetSymbolInfo(attr).Symbol is not IMethodSymbol ctor) return false;
            return SymbolEqualityComparer.Default.Equals(ctor.ContainingType, requirePermissionAttr);
        }

        /// <summary>提取 Define() 方法体中 <c>context.Add(new PermissionDefinition { Name = "..." })</c> 的字面量。</summary>
        private static IEnumerable<string> ExtractPermissionNames(MethodDeclarationSyntax defineMethod)
        {
            foreach (var invocation in defineMethod.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.ArgumentList?.Arguments.Count != 1) continue;
                if (invocation.ArgumentList.Arguments[0].Expression is not ObjectCreationExpressionSyntax oc) continue;
                if (!IsPermissionDefinitionType(oc.Type)) continue;
                if (oc.Initializer is not InitializerExpressionSyntax init) continue;

                foreach (var expr in init.Expressions)
                {
                    if (expr is not AssignmentExpressionSyntax assign) continue;
                    if (assign.Left is not IdentifierNameSyntax { Identifier.Text: "Name" }) continue;
                    if (assign.Right is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                        yield return lit.Token.ValueText;
                }
            }
        }

        /// <summary>类型语法是否为 PermissionDefinition（简单名或限定名结尾）。</summary>
        private static bool IsPermissionDefinitionType(TypeSyntax type)
        {
            var name = type switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                QualifiedNameSyntax q when q.Right is IdentifierNameSyntax right => right.Identifier.Text,
                _ => null
            };
            return name == PermissionDefinitionTypeName;
        }

        /// <summary>提取 [RequirePermission] 特性参数字符串（params string[]——逐参数；含位置）。</summary>
        private static IEnumerable<(Location Location, string Name)> ExtractAttributePermissionNames(AttributeSyntax attr)
        {
            if (attr.ArgumentList is null) yield break;
            foreach (var arg in attr.ArgumentList.Arguments)
            {
                if (arg.Expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                    yield return (lit.GetLocation(), lit.Token.ValueText);
            }
        }
    }
}