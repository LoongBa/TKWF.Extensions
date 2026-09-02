namespace TKWF.Ext.Tagging;

/// <summary>
/// 标签分析服务接口（占位）——标签存储扩展的分析门面。
/// <para><b>V0.3.0 实现</b>：基于 <see cref="ITagHitStore"/> 落库数据提供聚合分析：
/// 按维度 / 时间范围 / 标签名统计命中频次、趋势、占比。</para>
/// <para>当前仅声明契约（ADR52：Tag 算法回归 TKWF.Utility + Ext 瘦身为存储扩展；持久化迭代实施）。</para>
/// </summary>
public interface ITagAnalysisService
{
}