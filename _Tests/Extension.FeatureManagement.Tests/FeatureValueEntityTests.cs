using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// v0.3.0 Value 列加宽测试——D6（[MaxLength(512)] → MaxLength(-1)（FreeSql 无界字符串约定——SQLite text）；
/// 长 JSON 值（&gt;512 字符）落库/读回一致）+ C2（存量库加宽实测：FreeSql SyncStructure 自动 VARCHAR(512)→TEXT）。
/// </summary>
public class FeatureValueEntityTests
{
    private const string GrayConfigFeature = ConsumerFeatureContributor.JsonFeature;

    // ── D6 [MaxLength] 移除断言（反射） ──

    [Fact]
    public void Value_Property_HasNoBoundedMaxLengthConstraint()
    {
        var prop = typeof(FeatureValueEntity).GetProperty(nameof(FeatureValueEntity.Value))
            ?? throw new InvalidOperationException("FeatureValueEntity.Value 属性不存在");

        var maxLength = prop.GetCustomAttribute<MaxLengthAttribute>();

        // v0.3.0：Value 列不再受 VARCHAR(512) 限制——无 MaxLength 或 MaxLength(-1)（FreeSql 无界字符串约定）均通过
        if (maxLength != null)
            Assert.Equal(-1, maxLength.Length);
    }

    // ── D6 新装库 Value 列为 TEXT（SQLite 真实列） ──

    [Fact]
    public void NewInstall_ValueColumn_IsText()
    {
        var fsql = FeatureManagementTestSupport.CreateInMemoryFreeSql();
        try
        {
            FeatureManagementTestSupport.SyncStructure(fsql);

            var colType = (string?)fsql.Ado.ExecuteScalar(
                "SELECT type FROM pragma_table_info('FeatureValue') WHERE name = 'Value'");

            Assert.Equal("TEXT", colType);   // 无 MaxLength → FreeSql 默认映射 TEXT
        }
        finally
        {
            fsql.Dispose();
        }
    }

    // ── D6 长 JSON（>512 字符）落库 + 读回一致 ──

    [Fact]
    public async Task LongJson_Value_StoresAndReadsBack()
    {
        using var host = FeatureManagementTestHost.Create();
        // >512 字符的合法 JSON（旧 VARCHAR(512) 装不下）
        var longJson = JsonSerializer.Serialize(new
        {
            Description = new string('x', 600),
            Percent = 20
        });
        Assert.True(longJson.Length > 512, "测试数据须超过 512 字符");

        await host.Manager.SetValueAsync(GrayConfigFeature, longJson, FeatureProviders.Global, null, CancellationToken.None);

        // DB 实际存储完整字符串（列已加宽）
        var stored = await host.Store.GetAsync(GrayConfigFeature, FeatureProviders.Global, null, CancellationToken.None);
        Assert.NotNull(stored!.Value);
        Assert.True(stored.Value!.Length > 512);
        Assert.Equal(longJson, stored.Value);

        // 读回一致（字符串 + 类型化 Json 两条路径）
        var readBack = await host.Manager.GetValueAsync<string>(GrayConfigFeature, null, null, CancellationToken.None);
        Assert.Equal(longJson, readBack);

        var typed = await host.Manager.GetValueAsync<JsonElement>(GrayConfigFeature, null, default, CancellationToken.None);
        Assert.Equal(20, typed.GetProperty("Percent").GetInt32());
    }

    // ── C2 存量库迁移：模拟既有 VARCHAR(512) 表 → SyncStructure 加宽行为验证 ──

    [Fact]
    public void SyncStructure_ExistingVarchar512Table_ObservedBehavior()
    {
        // 模拟 v0.2.0 存量库：Value VARCHAR(512)（C2 评审修正——迁移行为依赖 SyncTables 实现，此处用 SQLite 实测）
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(false)
            .Build();
        try
        {
            fsql.Ado.ExecuteNonQuery("""
                CREATE TABLE FeatureValue (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name VARCHAR(128) NOT NULL,
                    Value VARCHAR(512) NULL,
                    ProviderName VARCHAR(32) NOT NULL,
                    ProviderKey VARCHAR(128) NULL,
                    Description VARCHAR(512) NULL,
                    IsVisibleToClients BOOLEAN NOT NULL DEFAULT 0,
                    CreateTime TIMESTAMP NOT NULL,
                    UpdateTime TIMESTAMP NOT NULL
                )
                """);

            fsql.CodeFirst.SyncStructure<FeatureValueEntity>();

            var colType = (string?)fsql.Ado.ExecuteScalar(
                "SELECT type FROM pragma_table_info('FeatureValue') WHERE name = 'Value'");

            // C2 实测结论：FreeSql SyncStructure（框架 SyncTables 底层机制）自动加宽存量 VARCHAR(512) → TEXT（SQLite 表重建）——
            // 存量库迁移路径成立；SQLite 实测 + DBA 手动 ALTER 兜底声明（跨方言视 FreeSql 实现，使用指南仍保留说明）
            Assert.Equal("TEXT", colType);
        }
        finally
        {
            fsql.Dispose();
        }
    }
}
