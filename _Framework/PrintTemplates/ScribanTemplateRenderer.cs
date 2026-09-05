using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Scriban;
using Scriban.Runtime;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// Scriban 沙箱渲染器——解析缓存 + MemberFilter 公共属性白名单 + 无 TemplateLoader + 递归 ScriptObject 转换。
    /// <para>Singleton 生命周期（无状态 + 解析缓存线程安全）。</para>
    /// </summary>
    internal sealed class ScribanTemplateRenderer : ITemplateRenderer
    {
        private readonly PrintTemplatesOptions _options;
        private readonly ConcurrentDictionary<string, Template> _parseCache = new();

        public ScribanTemplateRenderer(PrintTemplatesOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc />
        public Task<string> RenderContentAsync(string content, IReadOnlyDictionary<string, object?> model, CancellationToken ct = default)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (model == null) throw new ArgumentNullException(nameof(model));

            ct.ThrowIfCancellationRequested();

            // 1. 从解析缓存获取或编译模板
            var template = _parseCache.GetOrAdd(content, key =>
            {
                var parsed = Template.Parse(key);
                if (parsed.HasErrors)
                {
                    throw new TemplateParseException(
                        $"Scriban 模板解析失败: {string.Join("; ", parsed.Messages)}",
                        parsed.Messages.Select(m => m.ToString()));
                }
                return parsed;
            });

            // 2. 创建新的 TemplateContext（沙箱配置随上下文）
            var context = new TemplateContext
            {
                // 成员访问过滤：仅公共属性且有公共 getter（M3）
                MemberFilter = m => m is PropertyInfo p && p.GetMethod?.IsPublic == true,

                // 执行限制（从 Options 读取）
                LoopLimit = _options.LoopLimit,
                RecursiveLimit = _options.RecursiveLimit,
                LimitToString = _options.LimitToString,
                RegexTimeOut = TimeSpan.FromMilliseconds(_options.RegexTimeOut)
            };

            // 3. 不配置 TemplateLoader——模板正文中的 include 无法读盘（ADR 决策 2）

            // 4. 转换数据模型为 ScriptObject（递归，C5）
            var scriptModel = ConvertToScriptObject(model);
            context.PushGlobal(scriptModel);

            // 5. 渲染
            var result = template.Render(context);
            return Task.FromResult(result);
        }

        /// <summary>
        /// 递归转换数据模型为 Scriban ScriptObject（C5 安全契约）。
        /// <para>嵌套 IDictionary → ScriptObject；IList/数组 → ScriptArray；原始类型原样；其余转字符串（不暴露 .NET 对象）。</para>
        /// </summary>
        private static ScriptObject ConvertToScriptObject(IReadOnlyDictionary<string, object?> model)
        {
            var so = new ScriptObject();
            foreach (var kvp in model)
            {
                so.Add(kvp.Key, ConvertValue(kvp.Value));
            }
            return so;
        }

        private static object? ConvertValue(object? value)
        {
            if (value == null) return null;

            // 原始类型直接返回
            if (value is string or int or long or decimal or double or float or bool or byte or sbyte or short or ushort or uint or ulong)
                return value;

            // 嵌套字典 → ScriptObject（递归）
            if (value is IDictionary<string, object?> dict)
            {
                var so = new ScriptObject();
                foreach (var kvp in dict)
                    so.Add(kvp.Key, ConvertValue(kvp.Value));
                return so;
            }

            if (value is IDictionary dict2)
            {
                var so = new ScriptObject();
                foreach (DictionaryEntry entry in dict2)
                    so.Add(entry.Key?.ToString() ?? "", ConvertValue(entry.Value));
                return so;
            }

            // 列表/数组 → ScriptArray（递归）
            if (value is IEnumerable arr and not string)
            {
                var sa = new ScriptArray();
                foreach (var item in arr)
                    sa.Add(ConvertValue(item));
                return sa;
            }

            // 其余 .NET 类型 → 转字符串（不暴露原始对象）
            return value.ToString();
        }
    }
}
