using System;

namespace TKWF.Ext.Federation;

/// <summary>
/// 诊断 handler 标记（v0.12.0 排他单模型守卫）——标注"非业务"的 handler（如探针），
/// 供 <see cref="IEventOrchestrator"/> 排他校验排除计数：同一 ChannelType 下仅统计
/// 未标记的业务 handler，>1 即装配错误（fail-fast）。消费方自定义诊断类时应用本特性。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class DiagnosticEventReplyHandlerAttribute : Attribute
{
}
