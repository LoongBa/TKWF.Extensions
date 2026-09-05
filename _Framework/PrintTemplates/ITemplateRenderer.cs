using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 模板渲染器——Scriban 沙箱渲染（MemberFilter 公共属性白名单 + 无 TemplateLoader + 执行限制 + 解析缓存）。
    /// <para>安全契约（ADR 决策 2）：仅公共属性可访问；无文件读取；嵌套字典递归转换；解析失败抛 TemplateParseException。</para>
    /// </summary>
    public interface ITemplateRenderer
    {
        /// <summary>按模板正文 + 数据模型渲染（版本解析见 Manager；此处取已解析正文渲染）。</summary>
        Task<string> RenderContentAsync(string content, IReadOnlyDictionary<string, object?> model, CancellationToken ct = default);
    }
}
