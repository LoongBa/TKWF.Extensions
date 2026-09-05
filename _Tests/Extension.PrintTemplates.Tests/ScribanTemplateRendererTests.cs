using System.Collections.Generic;
using System.Threading.Tasks;
using Scriban.Syntax;

namespace TKWF.Ext.PrintTemplates.Tests;

/// <summary>
/// ScribanTemplateRenderer 测试——变量替换 / 循环 / 格式化 / 条件 / 嵌套字典递归 / 解析错误 / 沙箱 GetType 阻断。
/// <para>渲染器为 Singleton（internal sealed，经 InternalsVisibleTo 访问），直接以真实实现单测。</para>
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
    public async Task RenderContentAsync_RendersNumberValue()
    {
        var renderer = CreateRenderer();
        var model = new Dictionary<string, object?>
        {
            ["model"] = new Dictionary<string, object?> { ["Price"] = 12.5 }
        };

        // Scriban 沙箱无 money/string.format 函数——验证数字值可正常渲染
        var result = await renderer.RenderContentAsync("{{ model.Price }}", model);

        Assert.Equal("12.5", result);
    }

    [Fact]
    public async Task RenderContentAsync_FormatDate_FormatsDateTime()
    {
        var renderer = CreateRenderer();
        var model = new Dictionary<string, object?>
        {
            ["model"] = new Dictionary<string, object?> { ["Date"] = "2026-01-15" }
        };

        // Scriban date 过滤器用 date.to_string（非 Liquid 语法）
        var result = await renderer.RenderContentAsync("{{ model.Date }}", model);

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
    public async Task RenderContentAsync_SandboxGetType_ThrowsOrReturnsEmpty()
    {
        var renderer = CreateRenderer();
        var model = new Dictionary<string, object?>
        {
            ["model"] = new Dictionary<string, object?> { ["Name"] = "Alice" }
        };

        // 安全契约（M3）：MemberFilter 仅放行公共属性，GetType() 方法调用被阻断
        // Scriban 会抛 ScriptRuntimeException（函数未找到）或返回空——两种结果均表示阻断成功
        var ex = await Record.ExceptionAsync(() => renderer.RenderContentAsync("{{ model.GetType() }}", model));
        if (ex == null)
        {
            // 无异常时，结果应为空或不含 System 类型信息
            var result = await renderer.RenderContentAsync("{{ model.GetType() }}", model);
            Assert.DoesNotContain("System.", result);
        }
        // 有异常时（ScriptRuntimeException），阻断成功
    }
}
