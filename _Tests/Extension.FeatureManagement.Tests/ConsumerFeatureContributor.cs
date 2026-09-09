using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// 消费方 Feature 贡献者——模拟真实消费方在业务模块声明自己的功能开关定义。
/// <para>经 <c>[FeatureContributor]</c> 标记，SG1 编译期扫描消费方程序集生成
/// <c>GeneratedFeatureContributors</c> → <c>ProjectMetaContextBase.Instance.FeatureContributors</c>，
/// 扩展 <see cref="FeatureManagementExtensionInitializer{TUserInfo}.ConfigureServices"/> 运行时读取并实例化。</para>
/// <para>声明三种 ValueType 的测试 Feature（覆盖 Boolean/String/Int）：
/// <list type="bullet">
/// <item><c>Order.NewCheckout</c>——Boolean，默认 <c>false</c>（关闭）</item>
/// <item><c>App.Theme</c>——String，默认 <c>light</c></item>
/// <item><c>App.MaxItems</c>——Int，默认 <c>10</c></item>
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
    }
}
