namespace TKWF.Ext.Tagging;

/// <summary>
/// 标签命中存储接口（占位）——标签存储扩展的持久化契约。
/// <para><b>V0.3.0 实现</b>：命中结果经 <c>TagHitRecord</c>（SG1 实体，表 <c>TagHit</c>）落库，
/// 记录维度/标签/位置/原文快照/时间戳，支撑"高频标签/时间分布/维度占比"数据分析。</para>
/// <para>当前仅声明契约（ADR52：Tag 算法回归 TKWF.Utility + Ext 瘦身为存储扩展；持久化迭代实施）。</para>
/// </summary>
public interface ITagHitStore
{
}