using System.Collections.Generic;
using System.Threading.Tasks;
using Scriban.Syntax;

namespace TKWF.Ext.PrintTemplates.Tests;

/// <summary>
/// ScribanTemplateRenderer 测试——变量替换 / 循环 / 格式化 / 条件 / 嵌套字典递归 / 解析错误 / 沙箱 GetType 阻断。
/// <para>渲染器为 Singleton（internal sealed，经 InternalsVisibleTo 访问），直接以真实实现单测。</para>
/// <para>格式化为 Scriban 7 内建函数：货币用 object.format "C2"（.NET IFormattable），日期用 date.parse + object.format。</para>
/// </summary>
public class ScribanTemplateRendererTests
{
    /// <summary>创建默认 Options 的渲染器实例（每次测试独立，避免解析缓存跨测试污染）。</summary>
    private static ITemplateRenderer CreateRenderer(PrintTemplatesOptions? options = null)
        => new ScribanTemplateRenderer(options ?? new PrintTemplatesOptions());

    [Fact]
    public async Task RenderContentAsync_SimpleVariable_ReplacesVariable()
    {
        var renderer = CreateRenderer();
        var model = new Dictionary<string, object?>
        {
            ["model"] = new Dictionary<string, object?> { ["Name"] = "Alice" }
        };

        var result = await renderer.RenderContentAsync("Hello {{ model.Name }}", model);

        Assert.Equal("Hello Alice", result);
    }

    [Fact]
    public async Task RenderContentAsync_Loop_RendersItems()
    {
        var renderer = CreateRenderer();
        var model = new Dictionary<string, object?>
        {
            ["model"] = new Dictionary<string, object?>
            {
                ["Items"] = new List<string> { "A", "B", "C" }
            }
        };

        var result = await renderer.RenderContentAsync("{{ for item in model.Items }}[{{ item }}]{{ end }}", model);

        Assert.Equal("[A][B][C]", result);
    }

    [Fact]
    public async Task RenderContentAsync_FormatCurrency_FormatsNumber()
    {
        var renderer = CreateRenderer();
        var model = new Dictionary<string, object?>
        {
            ["model"] = new Dictionary<string, object?> { ["Price"] = 12.5 }
        };

        // Scriban 7 无 Liquid 式 money 过滤器——用 object.format（.NET IFormattable "C2" 货币格式）
        var result = await renderer.RenderContentAsync("{{ model.Price | object.format \"C2\" }}", model);

        // 货币符号随文化区域（$ / ¤ / ¥）而变——断言数字部分即可，保证跨机器稳定
        Assert.Contains("12.50", result);
    }

    [Fact]
    public async Task RenderContentAsync_FormatDate_FormatsDateTime()
    {
        var renderer = CreateRenderer();
        var model = new Dictionary<string, object?>
        {
            // ISO 字符串保证跨文化区域可解析，输出确定
            ["model"] = new Dictionary<string, object?> { ["Date"] = "2026-01-15" }
        };

        // Scriban 7：date.parse 解析字符串 → object.format 按 .NET 格式串输出（等价 strftime %Y-%m-%d）
        var result = await renderer.RenderContentAsync("{{ date.parse model.Date | object.format \"yyyy-MM-dd\" }}", model);

        Assert.Equal("2026-01-15", result);
    }

    [Fact]
    public async Task RenderContentAsync_Conditional_RendersBranch()
    {
        var renderer = CreateRenderer();
        var model = new Dictionary<string, object?>
        {
            ["model"] = new Dictionary<string, object?> { ["IsVip"] = true }
        };

        var result = await renderer.RenderContentAsync("{{ if model.IsVip }}VIP{{ else }}Normal{{ end }}", model);

        Assert.Equal("VIP", result);
    }

    [Fact]
    public async Task RenderContentAsync_NestedDictionary_ConvertsRecursively()
    {
        var renderer = CreateRenderer();
        var model = new Dictionary<string, object?>
        {
            ["model"] = new Dictionary<string, object?>
            {
                ["User"] = new Dictionary<string, object?> { ["Name"] = "Bob" }
            }
        };

        var result = await renderer.RenderContentAsync("{{ model.User.Name }}", model);

        Assert.Equal("Bob", result);
    }

    [Fact]
    public async Task RenderContentAsync_ParseError_ThrowsTemplateParseException()
    {
        var renderer = CreateRenderer();
        var model = new Dictionary<string, object?>
        {
            ["model"] = new Dictionary<string, object?> { ["Name"] = "Alice" }
        };

        // "{{ if }}" 缺少条件表达式——Scriban Template.Parse 报错 → 抛 TemplateParseException
        await Assert.ThrowsAsync<TemplateParseException>(
            () => renderer.RenderContentAsync("{{ if }}", model));
    }

    [Fact]
    public async Task RenderContentAsync_SandboxGetType_ReturnsEmpty()
    {
        var renderer = CreateRenderer();
        var model = new Dictionary<string, object?>
        {
            ["model"] = new Dictionary<string, object?> { ["Name"] = "Alice" }
        };

        // 安全契约（M3/M6）：MemberFilter 仅放行公共属性——GetType() 方法调用被阻断。
        // Scriban 对不可用函数抛 ScriptRuntimeException（"function model.GetType was not found"），
        // 渲染结果不会泄漏任何 .NET 类型信息（"empty/error" 双分支均代表阻断成功）。
        var ex = await Assert.ThrowsAsync<ScriptRuntimeException>(
            () => renderer.RenderContentAsync("{{ model.GetType() }}", model));

        Assert.Contains("GetType", ex.Message);
        Assert.DoesNotContain("System.", ex.Message);
    }
}
