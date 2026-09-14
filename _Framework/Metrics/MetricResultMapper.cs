using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TKW.Framework.Utility.Metrics;

namespace TKWF.Ext.Metrics
{
    /// <summary>
    /// 指标结果→标准化行映射器（静态，零依赖）。
    /// <para><b>后引擎不变量（C1）</b>：MetricsEngine.AddResult 已把 <see cref="MetricSlice"/>[] 展开为
    /// 多个 <see cref="MetricResult"/>（每切片一个，Value 为标量）——引擎输出恒为扁平。
    /// 本映射器只做 <b>1:1 MetricResult → MetricResultRow 投影</b> + DimensionsJson 序列化，
    /// <b>绝不处理 MetricSlice</b>（引擎已保证扁平，防御性处理为死代码，禁止引入）。</para>
    /// <para>消费方只需做"标准行 → 实体"映射，不碰 Dimensions 展开细节；映射器的
    /// <see cref="JsonOptions"/> 一并暴露，供消费方复用（如 Value 复杂对象 JSON 化）。</para>
    /// </summary>
    public static class MetricResultMapper
    {
        /// <summary>
        /// 序列化选项（§3.6 约定：<see cref="JsonIgnoreCondition.WhenWritingNull"/> + 紧凑输出）。
        /// <para>null 维度值不写入 JSON（{"bucket":"2026-08","segment":null} → {"bucket":"2026-08"}）；
        /// 暴露供消费方复用（如 Store 将复杂 Value 对象 JSON 化落库，对齐测试 Store 的 switch 第三路径）。</para>
        /// </summary>
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>
        /// MetricResult 列表 → 标准化行列表（1:1 投影 + Dimensions JSON 序列化；引擎已扁平化，无切片展开）。
        /// </summary>
        /// <param name="results">引擎输出结果列表（CalculateAsync 返回，已扁平）。</param>
        /// <param name="specKey">注入规格键（如 "sales"；默认 null，由调用方按业务传递，如从规格文件加载的 specKey）。</param>
        /// <param name="calculatedAtUtc">
        /// 注入计算时间（默认 <see cref="DateTime.UtcNow"/>——Mapper 调用时刻；调用方可传引擎完成时间覆盖，保证多行一致）。
        /// </param>
        /// <returns>与输入 1:1 对应的标准化行列表（空输入 → 空列表）。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> 为 null。</exception>
        public static List<MetricResultRow> ToRows(
            IReadOnlyList<MetricResult> results, string? specKey = null, DateTime? calculatedAtUtc = null)
        {
            ArgumentNullException.ThrowIfNull(results);

            var at = calculatedAtUtc ?? DateTime.UtcNow;
            if (results.Count == 0) return [];

            return results
                .Select(r => new MetricResultRow(
                    specKey,
                    r.Name,
                    r.Value,
                    r.Unit,
                    SerializeDimensions(r.Dimensions),
                    at))
                .ToList();
        }

        /// <summary>
        /// Dimensions → DimensionsJson（§3.6 序列化约定）。
        /// <para><c>Dimensions == null</c>（单值计算器无维度）→ null（与"无维度"语义一致，不写 "{}"）；
        /// 空字典 → "{}"（有 Dimensions 引用但无键）；非空 → JSON。
        /// <b>null 值维度不写入 JSON</b>（`{"bucket":"2026-08","segment":null}` → `{"bucket":"2026-08"}`）——
        /// System.Text.Json <see cref="JsonIgnoreCondition.WhenWritingNull"/> 仅对 POCO 属性生效、
        /// 对<b>字典项不生效</b>（实现经先过滤 null 值维度再序列化，对齐方案 §3.6 文档化输出约定）。</para>
        /// <para>类型保真：不保证反序列化类型保真（decimal→JSON number→可能变 double）——DimensionsJson
        /// 为存储态不透明字符串，消费方查询时按需解析。</para>
        /// </summary>
        private static string? SerializeDimensions(IReadOnlyDictionary<string, object?>? dimensions)
        {
            if (dimensions == null) return null;
            // §3.6：null 维度值省略（STJ WhenWritingNull 不覆盖字典项——先过滤再序列化）
            if (dimensions.Values.Any(v => v == null))
            {
                var filtered = new Dictionary<string, object?>(dimensions.Count);
                foreach (var kv in dimensions)
                {
                    if (kv.Value != null) filtered[kv.Key] = kv.Value;
                }
                return JsonSerializer.Serialize(filtered, JsonOptions);
            }
            return JsonSerializer.Serialize(dimensions, JsonOptions);
        }
    }
}