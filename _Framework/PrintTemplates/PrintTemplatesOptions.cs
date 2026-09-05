namespace TKWF.Ext.PrintTemplates
{
    /// <summary>
    /// 打印模板扩展配置——绑定 TKWF:PrintTemplates 配置节。
    /// <para>Scriban 沙箱执行限制（默认值：LoopLimit=1000 / RecursiveLimit=100 / LimitToString=1MB / RegexTimeOut=10s）。</para>
    /// </summary>
    public class PrintTemplatesOptions
    {
        /// <summary>模板循环上限（Scriban LoopLimit）。</summary>
        public int LoopLimit { get; set; } = 1000;

        /// <summary>模板递归上限（Scriban RecursiveLimit）。</summary>
        public int RecursiveLimit { get; set; } = 100;

        /// <summary>字符串转换上限（Scriban LimitToString，默认 1MB）。</summary>
        public int LimitToString { get; set; } = 1048576;

        /// <summary>正则表达式超时（Scriban RegexTimeOut，默认 10s）。</summary>
        public int RegexTimeOut { get; set; } = 10000;
    }
}
