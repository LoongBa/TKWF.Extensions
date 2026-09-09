using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// 消费方 Feature 贡献者——模拟真实消费方在业务模块声明自己的功能开关定义。
/// <para>经 <c>[FeatureContributor]</c> 标记，SG1 编译期扫描消费方程序集生成
/// <c>GeneratedFeatureContributors</c> → <c>ProjectMetaContextBase.Instance.FeatureContributors</c>，
/// 扩展 <see cref="FeatureManagementExtensionInitializer{TUserInfo}.ConfigureServices"/> 运行时读取并实例化。</para>
/// <para>声明六种 ValueType 的测试 Feature（覆盖 Boolean/String/Int/Decimal/DateTime/Json——v0.3.0）：
/// <list type="bullet">
/// <item><c>Order.NewCheckout</c>——Boolean，默认 <c>false</c>（关闭）</item>
/// <item><c>App.Theme</c>——String，默认 <c>light</c></item>
/// <item><c>App.MaxItems</c>——Int，默认 <c>10</c></item>
/// <item><c>App.Rate</c>——Decimal，默认 <c>1.5</c>（v0.3.0）</item>
/// <item><c>App.MaintenanceWindow</c>——DateTime，默认 <c>2026-10-01T00:00:00Z</c>（v0.3.0，ISO8601）</item>
/// <item><c>App.GrayConfig</c>——Json，默认 <c>{"percent":20}</c>（v0.3.0，合法 JSON）</item>
/// </list></para>
/// </summary>
[FeatureContributor]
public class ConsumerFeatureContributor : IFeatureDefinitionContributor
{
    /// <summary>Boolean 测试 Feature 名（默认 false）。</summary>
    public const string BooleanFeature = "Order.NewCheckout";

    /// <summary>String 测试 Feature 名（默认 light）。</summary>
    public const string StringFeature = "App.Theme";

    /// <summary>Int 测试 Feature 名（默认 10）。</summary>
    public const string IntFeature = "App.MaxItems";

    /// <summary>Decimal 测试 Feature 名（默认 1.5——v0.3.0）。</summary>
    public const string DecimalFeature = "App.Rate";

    /// <summary>DateTime 测试 Feature 名（默认 2026-10-01T00:00:00Z——v0.3.0，ISO8601 UTC 契约）。</summary>
    public const string DateTimeFeature = "App.MaintenanceWindow";

    /// <summary>Json 测试 Feature 名（默认 {"percent":20}——v0.3.0，合法 JSON）。</summary>
    public const string JsonFeature = "App.GrayConfig";

    public void Define(FeatureDefinitionContext context)
    {
        context.Add(new FeatureDefinition
        {
            Name = BooleanFeature,
            DisplayName = "新结算流程",
            Description = "启用新的结算流程（Boolean 示例）",
            Group = "Order",
            ValueType = FeatureValueType.Boolean,
            DefaultValue = "false"
        });
        context.Add(new FeatureDefinition
        {
            Name = StringFeature,
            DisplayName = "界面主题",
            Group = "App",
            ValueType = FeatureValueType.String,
            DefaultValue = "light"
        });
        context.Add(new FeatureDefinition
        {
            Name = IntFeature,
            DisplayName = "每页最大条数",
            Group = "App",
            ValueType = FeatureValueType.Int,
            DefaultValue = "10"
        });
        context.Add(new FeatureDefinition
        {
            Name = DecimalFeature,
            DisplayName = "费率（每笔）",
            Description = "单笔费率/阈值（Decimal 示例——v0.3.0）",
            Group = "App",
            ValueType = FeatureValueType.Decimal,
            DefaultValue = "1.5"
        });
        context.Add(new FeatureDefinition
        {
            Name = DateTimeFeature,
            DisplayName = "计划维护窗口",
            Description = "计划切换/维护生效时间（DateTime 示例——v0.3.0，ISO8601）",
            Group = "App",
            ValueType = FeatureValueType.DateTime,
            DefaultValue = "2026-10-01T00:00:00Z"
        });
        context.Add(new FeatureDefinition
        {
            Name = JsonFeature,
            DisplayName = "灰度发布配置",
            Description = "灰度发布参数（Json 示例——v0.3.0，对象/数组）",
            Group = "App",
            ValueType = FeatureValueType.Json,
            DefaultValue = """{"percent":20}"""
        });
    }
}
