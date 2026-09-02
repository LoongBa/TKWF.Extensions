namespace TKWF.Ext.Tagging;

/// <summary>
/// 标签规则存储接口（占位）——标签存储扩展的持久化契约。
/// <para><b>V0.3.0 实现</b>：规则经 <c>TagRuleEntity</c>（SG1 实体，表 <c>TagRule</c>）持久化，
/// 支持管理端维护（维度/匹配模式/模式串/启用/优先级/互斥组/默认标签）。</para>
/// <para>当前仅声明契约（ADR52：Tag 算法回归 TKWF.Utility + Ext 瘦身为存储扩展；持久化迭代实施）。</para>
/// </summary>
public interface ITagRuleStore
{
}