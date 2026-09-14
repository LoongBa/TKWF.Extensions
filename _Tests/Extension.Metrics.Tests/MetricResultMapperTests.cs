using System;
using System.Collections.Generic;
using TKW.Framework.Utility.Metrics;
using TKWF.Ext.Metrics;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// MetricResultMapper（v0.2.0）测试——MetricResult → MetricResultRow 1:1 投影 + DimensionsJson 序列化
/// （§3.6 约定）+ calculatedAtUtc 注入。
/// <para>C1：引擎已扁平化（MetricsEngine.AddResult 展开 MetricSlice[]）——本测试只测 MetricResult 输入
/// （引擎已展开切片形态，即多个已有标量 Value 的 MetricResult），不测 MetricSlice[]（映射器不处理切片）。</para>
/// </summary>
public class MetricResultMapperTests
{
    [Fact]
    public void ToRows_SingleResult_ProducesSingleRow_AllFieldsPassthrough()
    {
        var result = new MetricResult(
            "repurchase-rate-30d", 0.25m, "%",
            new Dictionary<string, object?> { ["bucket"] = "2026-08", ["segment"] = "vip" });
        var at = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        var rows = MetricResultMapper.ToRows([result], specKey: "sales", calculatedAtUtc: at);

        var row = Assert.Single(rows);
        Assert.Equal("sales", row.SpecKey);
        Assert.Equal("repurchase-rate-30d", row.Name);
        Assert.Equal(0.25m, row.Value);
        Assert.Equal("%", row.Unit);
        Assert.Equal("""{"bucket":"2026-08","segment":"vip"}""", row.DimensionsJson);
        Assert.Equal(at, row.CalculatedAtUtc);
    }

    [Fact]
    public void ToRows_MultipleResults_EngineFlattenedSliceShape_MapsOneToOne()
    {
        // 引擎已展开切片形态：同 Name 多个标量 Value 的 MetricResult（每切片一个，各带自身 Dimensions）
        var results = new List<MetricResult>
        {
            new("sales-by-day", 100m, "CNY", new Dictionary<string, object?> { ["bucket"] = "2026-08-01" }),
            new("sales-by-day", 150m, "CNY", new Dictionary<string, object?> { ["bucket"] = "2026-08-02" }),
            new("sales-by-day", 80m, "CNY", new Dictionary<string, object?> { ["bucket"] = "2026-08-03" }),
        };
        var at = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        var rows = MetricResultMapper.ToRows(results, specKey: "dashboard", calculatedAtUtc: at);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("dashboard", r.SpecKey));
        Assert.All(rows, r => Assert.Equal("sales-by-day", r.Name));
        Assert.All(rows, r => Assert.Equal(at, r.CalculatedAtUtc));
        Assert.Equal("""{"bucket":"2026-08-01"}""", rows[0].DimensionsJson);
        Assert.Equal("""{"bucket":"2026-08-02"}""", rows[1].DimensionsJson);
        Assert.Equal("""{"bucket":"2026-08-03"}""", rows[2].DimensionsJson);
        Assert.Equal(100m, rows[0].Value);
        Assert.Equal(150m, rows[1].Value);
        Assert.Equal(80m, rows[2].Value);
    }

    [Fact]
    public void ToRows_DimensionsJsonSerialization_Nested_Empty_NullValue()
    {
        // 嵌套字典
        var nested = new MetricResult("nested", 1m, null,
            new Dictionary<string, object?>
            {
                ["bucket"] = new Dictionary<string, object?> { ["month"] = "2026-08" },
                ["segment"] = "vip"
            });
        // 空字典
        var empty = new MetricResult("empty", 2m, null, new Dictionary<string, object?>());
        // 字典含 null 值（WhenWritingNull 省略——"segment":null 不写入）
        var nullVal = new MetricResult("nullval", 3m, null,
            new Dictionary<string, object?> { ["bucket"] = "2026-08", ["segment"] = null });

        var rows = MetricResultMapper.ToRows(new[] { nested, empty, nullVal });

        Assert.Equal("""{"bucket":{"month":"2026-08"},"segment":"vip"}""", rows[0].DimensionsJson);
        Assert.Equal("{}", rows[1].DimensionsJson);
        Assert.Equal("""{"bucket":"2026-08"}""", rows[2].DimensionsJson); // null 值维度省略
    }

    [Fact]
    public void ToRows_EmptyList_ReturnsEmptyList()
    {
        var rows = MetricResultMapper.ToRows([]);

        Assert.Empty(rows);
    }

    [Fact]
    public void ToRows_ValueNull_ProducesNullValueRow()
    {
        var result = new MetricResult("denominator-zero", null, "CNY");

        var row = Assert.Single(MetricResultMapper.ToRows([result]));

        Assert.Null(row.Value);
        Assert.Equal("denominator-zero", row.Name);
        Assert.Equal("CNY", row.Unit);
    }

    [Fact]
    public void ToRows_SpecKeyPassthrough_And_ExplicitCalculatedAtUtcOverridesDefault()
    {
        var result = new MetricResult("aov-cny", 123.45m, "CNY");
        var specified = new DateTime(2026, 9, 2, 8, 30, 0, DateTimeKind.Utc);

        var rows = MetricResultMapper.ToRows([result], specKey: "order-stat", calculatedAtUtc: specified);

        var row = Assert.Single(rows);
        Assert.Equal("order-stat", row.SpecKey);
        Assert.Equal(specified, row.CalculatedAtUtc);

        // 未传 calculatedAtUtc → 默认 DateTime.UtcNow（容差秒级，防极慢测试机误判）
        var defaultRows = MetricResultMapper.ToRows([result]);
        Assert.True((DateTime.UtcNow - defaultRows[0].CalculatedAtUtc).Duration() < TimeSpan.FromSeconds(5));
    }
}