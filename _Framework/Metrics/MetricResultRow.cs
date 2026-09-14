using System;

namespace TKWF.Ext.Metrics
{
    /// <summary>
    /// 指标结果标准化行 DTO——持久化契约的载体，消费方 Store 将其映射为自身实体落库。
    /// <para>契约与实体解耦（ADR-Metrics-指标结果持久化契约与实体归属 决策 3）：本 DTO 不包含任何
    /// 实体类型，扩展无法感知消费方表结构；扩展只负责经 <see cref="MetricResultMapper.ToRows"/>
    /// 将引擎输出的 <see cref="TKW.Framework.Utility.Metrics.MetricResult"/> 展开为标准化行。</para>
    /// </summary>
    /// <param name="SpecKey">规格键（如 "sales"），可空。</param>
    /// <param name="Name">指标名（如 "repurchase-rate-30d"）。</param>
    /// <param name="Value">
    /// 指标值。
    /// <para><b>后引擎不变量（C1/P3）</b>：经引擎计算 + <see cref="MetricResultMapper"/> 投影后，
    /// Value 恒为<b>标量</b>（decimal/double/string/null）——引擎已将 <c>MetricSlice[]</c> 展开为
    /// 多个 MetricResult（每切片一个，Value 为标量），映射器不做任何切片展开。契约保留
    /// <c>object?</c> 以容纳消费方自定义计算器/直连 calculator 的复杂值（序列化由消费方 Store 负责）。</para>
    /// </param>
    /// <param name="Unit">单位（如 "%"、"CNY"），可空。</param>
    /// <param name="DimensionsJson">维度 JSON（如 {"bucket":"2026-08-01","segment":"vip"}），可空。
    /// <c>null</c> = 无维度（单值计算器）；空字典序列化为 "{}"。序列化约定见 <see cref="MetricResultMapper"/>。</param>
    /// <param name="CalculatedAtUtc">计算时间（UTC）。</param>
    public sealed record MetricResultRow(
        string? SpecKey,
        string Name,
        object? Value,
        string? Unit,
        string? DimensionsJson,
        DateTime CalculatedAtUtc);
}