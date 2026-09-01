using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TKWF.Ext.Permissions.Validation.Tests;

/// <summary>
/// Analyzer 测试辅助——将源码字符串编译为 CSharpCompilation，挂载被测 Analyzer，收集诊断。
/// </summary>
public static class AnalyzerTestHelper
{
    private const string TestProjectName = "TestProject";

    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    private static readonly ImmutableArray<MetadataReference> ReferencesWithoutAbstractions =
        BuildReferences(includeAbstractions: false);

    private static ImmutableArray<MetadataReference> BuildReferences(bool includeAbstractions = true)
    {
        // 运行时程序集（trusted platform assemblies）
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToImmutableArray()
            ?? ImmutableArray<MetadataReference>.Empty;

        // 主框架 Domain（RequirePermissionAttribute 基类 DomainFlagAttribute + IUserInfo）
        var domain = MetadataReference.CreateFromFile(
            typeof(TKW.Framework.Domain.Interfaces.IUserInfo).Assembly.Location);

        var result = trusted.Add(domain);

        if (includeAbstractions)
        {
            var abstractions = MetadataReference.CreateFromFile(
                typeof(TKWF.Ext.Permissions.Abstractions.PermissionContributorAttribute).Assembly.Location);
            result = result.Add(abstractions);
        }

        return result;
    }

    /// <summary>编译源码 + 运行 Analyzer，返回指定 ID 的诊断（默认 PERM001）。</summary>
    public static ImmutableArray<Diagnostic> RunAnalyzer(
        string source,
        DiagnosticAnalyzer analyzer,
        string? diagnosticId = null,
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        var references = References;
        if (extraReferences is not null)
            references = references.AddRange(extraReferences);

        return RunCore(source, analyzer, diagnosticId, references);
    }

    /// <summary>不带 Abstractions 引用的编译（验证未引用契约时静默跳过）。</summary>
    public static ImmutableArray<Diagnostic> RunAnalyzerWithoutAbstractions(
        string source,
        DiagnosticAnalyzer analyzer,
        string? diagnosticId = null)
        => RunCore(source, analyzer, diagnosticId, ReferencesWithoutAbstractions);

    /// <summary>将源码编译为内存程序集并返回 MetadataReference（用于模拟 DLL 贡献者）。</summary>
    public static PortableExecutableReference CompileToReference(string source, string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
            throw new InvalidOperationException(
                $"引用程序集编译失败: {string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()))}");

        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream, filePath: assemblyName + ".dll");
    }

    private static ImmutableArray<Diagnostic> RunCore(
        string source,
        DiagnosticAnalyzer analyzer,
        string? diagnosticId,
        ImmutableArray<MetadataReference> references)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            TestProjectName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        var allDiagnostics = compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();

        return diagnosticId is null
            ? allDiagnostics
            : allDiagnostics.Where(d => d.Id == diagnosticId).ToImmutableArray();
    }
}