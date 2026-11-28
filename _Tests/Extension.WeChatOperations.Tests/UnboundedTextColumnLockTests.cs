using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// 无界文本列 [MaxLength(-1)] 回归锁（2026-11-23 code-review P1，commit ca7db50 / Pg 22001 事故固化）：
/// 7 列承载完整菜单 JSON / 文章正文 / 回复 JSON / 推演明细——FreeSql 缺省映射 varchar(255)，
/// 一旦有人删掉 [MaxLength(-1)] 即静默退回有界列，生产超长内容触发 Pg 22001（2026-11-14 首发命中）。
/// <para>反射断言而非仅看源码——属性被移除即红；同时锁定"恰好这 7 列"防止无界面意外扩张。</para>
/// </summary>
public sealed class UnboundedTextColumnLockTests
{
    private const int Unbounded = -1;

    /// <summary>7 列逐一断言存在 [MaxLength] 且 Length == -1（移除属性 → Assert.NotNull 失败）。</summary>
    [Theory]
    [InlineData(typeof(MenuSnapshotEntity), nameof(MenuSnapshotEntity.MenuJson))]
    [InlineData(typeof(MenuVersionEntity), nameof(MenuVersionEntity.BeforeJson))]
    [InlineData(typeof(MenuVersionEntity), nameof(MenuVersionEntity.AfterJson))]
    [InlineData(typeof(MaterialEntity), nameof(MaterialEntity.Content))]
    [InlineData(typeof(InteractionLogEntity), nameof(InteractionLogEntity.Content))]
    [InlineData(typeof(InteractionLogEntity), nameof(InteractionLogEntity.ReplyContent))]
    [InlineData(typeof(InteractionLogEntity), nameof(InteractionLogEntity.MatchTraceJson))]
    public void Column_Has_Unbounded_MaxLength(Type entityType, string propertyName)
    {
        var property = entityType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{entityType.Name}.{propertyName} 属性不存在");

        var attr = property.GetCustomAttribute<MaxLengthAttribute>();
        Assert.NotNull(attr);   // 属性被删 → 红（回归锁核心断言）
        Assert.Equal(Unbounded, attr!.Length);
    }

    /// <summary>恰好这 7 列无界——四类实体上扫描所有 [MaxLength(-1)] 字符串属性，集合与期望完全一致。</summary>
    [Fact]
    public void Unbounded_Column_Set_Is_Exactly_Seven()
    {
        var entityTypes = new[]
        {
            typeof(MenuSnapshotEntity), typeof(MenuVersionEntity),
            typeof(MaterialEntity), typeof(InteractionLogEntity),
        };

        var actual = entityTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string)
                    && p.GetCustomAttribute<MaxLengthAttribute>()?.Length == Unbounded)
                .Select(p => $"{t.Name}.{p.Name}"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "InteractionLogEntity.Content",
            "InteractionLogEntity.MatchTraceJson",
            "InteractionLogEntity.ReplyContent",
            "MaterialEntity.Content",
            "MenuSnapshotEntity.MenuJson",
            "MenuVersionEntity.AfterJson",
            "MenuVersionEntity.BeforeJson",
        };

        Assert.Equal(expected, actual);
    }
}
