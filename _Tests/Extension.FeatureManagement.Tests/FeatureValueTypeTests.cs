using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>Json 复杂值 POCO——FeatureValueType.Json 测试用（JsonSerializer 默认大小写敏感匹配 PascalCase 属性）。</summary>
public sealed class GrayConfig
{
    public int Percent { get; set; }
    public string[]? Groups { get; set; }
}

/// <summary>
/// v0.3.0 类型化 ValueType 测试——D1-D5（枚举扩展 / 六类型往返 / 规范序列化 / 写时校验拒绝 / 未定义跳过校验）。
/// </summary>
public class FeatureValueTypeTests
{
    private const string Theme = ConsumerFeatureContributor.StringFeature;
    private const string Checkout = ConsumerFeatureContributor.BooleanFeature;
    private const string MaxItems = ConsumerFeatureContributor.IntFeature;
    private const string Rate = ConsumerFeatureContributor.DecimalFeature;
    private const string MaintenanceWindow = ConsumerFeatureContributor.DateTimeFeature;
    private const string GrayConfigFeature = ConsumerFeatureContributor.JsonFeature;

    // ── D1 枚举扩展（Decimal/DateTime/Json 可声明） ──

    [Fact]
    public void Enum_DeclaresV030Types()
    {
        Assert.True(Enum.IsDefined(typeof(FeatureValueType), FeatureValueType.Decimal));
        Assert.True(Enum.IsDefined(typeof(FeatureValueType), FeatureValueType.DateTime));
        Assert.True(Enum.IsDefined(typeof(FeatureValueType), FeatureValueType.Json));
        // 既有类型保留（零迁移）
        Assert.True(Enum.IsDefined(typeof(FeatureValueType), FeatureValueType.Boolean));
        Assert.True(Enum.IsDefined(typeof(FeatureValueType), FeatureValueType.String));
        Assert.True(Enum.IsDefined(typeof(FeatureValueType), FeatureValueType.Int));
    }

    // ── D2 类型化读取：六类型往返 + 解析失败 → defaultValue ──

    [Fact]
    public async Task GetValueAsync_Bool_RoundTrip()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Checkout, true, FeatureProviders.Global, null, CancellationToken.None);

        var result = await host.Manager.GetValueAsync<bool>(Checkout, null, false, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task GetValueAsync_Int_RoundTrip()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(MaxItems, 42, FeatureProviders.Global, null, CancellationToken.None);

        var result = await host.Manager.GetValueAsync<int>(MaxItems, null, 0, CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task GetValueAsync_Decimal_RoundTrip()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Rate, 1.5m, FeatureProviders.Global, null, CancellationToken.None);

        var result = await host.Manager.GetValueAsync<decimal>(Rate, null, 0m, CancellationToken.None);

        Assert.Equal(1.5m, result);
    }

    [Fact]
    public async Task GetValueAsync_DateTime_RoundTrip()
    {
        using var host = FeatureManagementTestHost.Create();
        var utc = new DateTime(2026, 10, 1, 8, 30, 15, DateTimeKind.Utc);
        await host.Manager.SetValueAsync(MaintenanceWindow, utc, FeatureProviders.Global, null, CancellationToken.None);

        var result = await host.Manager.GetValueAsync<DateTime>(MaintenanceWindow, null, default, CancellationToken.None);

        Assert.Equal(utc, result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);   // ISO8601 "O" + RoundtripKind 保留 Kind
    }

    [Fact]
    public async Task GetValueAsync_JsonReferenceType_RoundTrip()
    {
        using var host = FeatureManagementTestHost.Create();
        var config = new GrayConfig { Percent = 20, Groups = ["beta", "gamma"] };
        await host.Manager.SetValueAsync(GrayConfigFeature, config, FeatureProviders.Global, null, CancellationToken.None);

        var result = await host.Manager.GetValueAsync<GrayConfig>(GrayConfigFeature, null, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(20, result!.Percent);
        Assert.Equal(["beta", "gamma"], result.Groups);
    }

    [Fact]
    public async Task GetValueAsync_String_RoundTrip()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);

        var result = await host.Manager.GetValueAsync<string>(Theme, null, null, CancellationToken.None);

        Assert.Equal("dark", result);
    }

    [Fact]
    public async Task GetValueAsync_IntParseFailure_ReturnsDefault_NoThrow()
    {
        // 解析失败 → defaultValue（不抛）——String 特征存 "abc" 后按 int 读
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Theme, "abc", FeatureProviders.Global, null, CancellationToken.None);

        var result = await host.Manager.GetValueAsync<int>(Theme, null, 42, CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task GetValueAsync_DecimalParseFailure_ReturnsDefault_NoThrow()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Theme, "abc", FeatureProviders.Global, null, CancellationToken.None);

        var result = await host.Manager.GetValueAsync<decimal>(Theme, null, 9.9m, CancellationToken.None);

        Assert.Equal(9.9m, result);
    }

    [Fact]
    public async Task GetValueAsync_DateTimeParseFailure_ReturnsDefault_NoThrow()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Theme, "not-a-date", FeatureProviders.Global, null, CancellationToken.None);

        var result = await host.Manager.GetValueAsync<DateTime>(Theme, null, new DateTime(2000, 1, 1), CancellationToken.None);

        Assert.Equal(new DateTime(2000, 1, 1), result);
    }

    [Fact]
    public async Task GetValueAsync_JsonReferenceTypeParseFailure_ReturnsNull()
    {
        // P2：Json 引用类型解析失败 → null（不抛）——经 DataService 直写非法 JSON（绕过写时校验，模拟存量坏值）
        using var host = FeatureManagementTestHost.Create();
        await host.DataService.UpsertByKeyAsync(new FeatureValueEntity
        {
            Name = GrayConfigFeature,
            Value = "{invalid-json",
            ProviderName = FeatureProviders.Global,
            ProviderKey = null,
            UpdateTime = DateTime.UtcNow
        }, CancellationToken.None);

        var result = await host.Manager.GetValueAsync<GrayConfig>(GrayConfigFeature, null, null, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetValueAsync_NoValue_ReturnsDefinitionDefault()
    {
        // 无存储值 → 定义 DefaultValue（"1.5"）→ 类型化解析返回 1.5（与字符串入口回退链一致）
        using var host = FeatureManagementTestHost.Create();

        var result = await host.Manager.GetValueAsync<decimal>(Rate, null, 0m, CancellationToken.None);

        Assert.Equal(1.5m, result);
    }

    // ── D3 类型化写入：规范序列化形式 ──

    [Fact]
    public async Task SetValueAsync_Bool_StoresLowercaseCanonical()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Checkout, true, FeatureProviders.Global, null, CancellationToken.None);

        var stored = await host.Store.GetAsync(Checkout, FeatureProviders.Global, null, CancellationToken.None);

        Assert.Equal("true", stored!.Value);   // 规范小写

        await host.Manager.SetValueAsync(Checkout, false, FeatureProviders.Global, null, CancellationToken.None);
        Assert.Equal("false", (await host.Store.GetAsync(Checkout, FeatureProviders.Global, null, CancellationToken.None))!.Value);
    }

    [Fact]
    public async Task SetValueAsync_DateTime_StoresIso8601()
    {
        using var host = FeatureManagementTestHost.Create();
        var utc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        await host.Manager.SetValueAsync(MaintenanceWindow, utc, FeatureProviders.Global, null, CancellationToken.None);

        var stored = await host.Store.GetAsync(MaintenanceWindow, FeatureProviders.Global, null, CancellationToken.None);

        Assert.Equal(utc.ToString("O", CultureInfo.InvariantCulture), stored!.Value);   // ISO8601
    }

    [Fact]
    public async Task SetValueAsync_Json_StoresNormalizedJson()
    {
        using var host = FeatureManagementTestHost.Create();
        var config = new GrayConfig { Percent = 20, Groups = ["beta"] };
        await host.Manager.SetValueAsync(GrayConfigFeature, config, FeatureProviders.Global, null, CancellationToken.None);

        var stored = await host.Store.GetAsync(GrayConfigFeature, FeatureProviders.Global, null, CancellationToken.None);

        Assert.NotNull(stored!.Value);
        using var doc = JsonDocument.Parse(stored.Value!);   // 规范 JSON 文档（可解析）
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal(20, JsonSerializer.Deserialize<GrayConfig>(stored.Value!)!.Percent);
    }

    // ── D4 写时校验（定义 ValueType 违反 → ArgumentException） ──

    [Fact]
    public async Task SetValue_InvalidBoolean_ThrowsArgumentException()
    {
        using var host = FeatureManagementTestHost.Create();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            host.Manager.SetValueAsync(Checkout, "abc", FeatureProviders.Global, null, CancellationToken.None));
    }

    [Fact]
    public async Task SetValue_InvalidInt_ThrowsArgumentException()
    {
        using var host = FeatureManagementTestHost.Create();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            host.Manager.SetValueAsync(MaxItems, "12.5", FeatureProviders.Global, null, CancellationToken.None));
    }

    [Fact]
    public async Task SetValue_InvalidDecimal_ThrowsArgumentException()
    {
        using var host = FeatureManagementTestHost.Create();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            host.Manager.SetValueAsync(Rate, "abc", FeatureProviders.Global, null, CancellationToken.None));
    }

    [Fact]
    public async Task SetValue_InvalidDateTime_ThrowsArgumentException()
    {
        using var host = FeatureManagementTestHost.Create();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            host.Manager.SetValueAsync(MaintenanceWindow, "not-a-date", FeatureProviders.Global, null, CancellationToken.None));
    }

    [Fact]
    public async Task SetValue_InvalidJson_ThrowsArgumentException()
    {
        using var host = FeatureManagementTestHost.Create();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            host.Manager.SetValueAsync(GrayConfigFeature, "{", FeatureProviders.Global, null, CancellationToken.None));
    }

    [Fact]
    public async Task SetValue_String_AnyValue_Passes()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Theme, "任意字符串", FeatureProviders.Global, null, CancellationToken.None);
        Assert.Equal("任意字符串", await host.Manager.GetValueAsync(Theme, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task SetValueTyped_WrongTypeForDefinition_ThrowsArgumentException()
    {
        // 类型化写入同样受写时校验约束：向 Decimal 特征写 bool true → 序列化 "true" → decimal.TryParse 失败
        using var host = FeatureManagementTestHost.Create();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            host.Manager.SetValueAsync(Rate, true, FeatureProviders.Global, null, CancellationToken.None));
    }

    // ── D5 未定义 Feature 写值 → 跳过校验（向后兼容） ──

    [Fact]
    public async Task SetValue_UndefinedFeature_SkipsValidation_NoThrow()
    {
        using var host = FeatureManagementTestHost.Create();
        const string undefined = "App.NotDefined";

        await host.Manager.SetValueAsync(undefined, "abc", FeatureProviders.Global, null, CancellationToken.None);

        // 未定义 Feature 任意字符串可写（v0.2.0 无定义可写语义保留）
        Assert.Equal("abc", await host.Manager.GetValueAsync(undefined, null, null, CancellationToken.None));
    }
}
