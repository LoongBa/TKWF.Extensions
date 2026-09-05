using System;
using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 模板解析异常——Scriban Template.Parse 返回 HasErrors/Messages 时抛出。
    /// <para>包含解析消息列表（调试用）。</para>
    /// </summary>
    public class TemplateParseException : Exception
    {
        /// <summary>解析消息列表。</summary>
        public IReadOnlyList<string> Messages { get; }

        public TemplateParseException(string message, IEnumerable<string> messages)
            : base(message)
        {
            Messages = (messages?.Where(m => !string.IsNullOrEmpty(m)).ToList() is { Count: > 0 } list)
                ? list.AsReadOnly()
                : (IReadOnlyList<string>)Array.Empty<string>();
        }
    }
}
