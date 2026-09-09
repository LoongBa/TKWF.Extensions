using System;
using System.Linq;
using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// FeatureDefinition 测试——D2 定义收集/校验。
/// <para>覆盖：Contributor Define → repository 含全部定义（宿主构造后）；FeatureDefinitionContext
/// 唯一校验（重复 Name → InvalidOperationException；空 Name → ArgumentException）。</para>
/// </summary>
public class FeatureDefinitionTests
{
    // ── D2 定义收集（宿主构造后 repository 含 Contributor 声明定义） ──

    [Fact]
    public void HostRepository_ContainsContributorDefinitions()
    {
        using var host = FeatureManagementTestHost.Create();

        var definitions = host.DefinitionRepository.GetAll();

        Assert.Equal(3, definitions.Count);
        Assert.Contains(definitions, d => d.Name == ConsumerFeatureContributor.BooleanFeature);
        Assert.Contains(definitions, d => d.Name == ConsumerFeatureContributor.StringFeature);
        Assert.Contains(definitions, d => d.Name == ConsumerFeatureContributor.IntFeature);
    }

    [Fact]
    public void HostRepository_BooleanDefinition_MetadataCollected()
    {
        using var host = FeatureManagementTestHost.Create();

        var definition = host.DefinitionRepository.GetAll()
            .Single(d => d.Name == ConsumerFeatureContributor.BooleanFeature);

        Assert.Equal(FeatureValueType.Boolean, definition.ValueType);
        Assert.Equal("false", definition.DefaultValue);
        Assert.Equal("Order", definition.Group);
        Assert.Equal("新结算流程", definition.DisplayName);
    }

    [Fact]
    public void HostRepository_StringDefinition_MetadataCollected()
    {
        using var host = FeatureManagementTestHost.Create();

        var definition = host.DefinitionRepository.GetAll()
            .Single(d => d.Name == ConsumerFeatureContributor.StringFeature);

        Assert.Equal(FeatureValueType.String, definition.ValueType);
        Assert.Equal("light", definition.DefaultValue);
        Assert.Equal("App", definition.Group);
    }

    [Fact]
    public void HostRepository_IntDefinition_MetadataCollected()
    {
        using var host = FeatureManagementTestHost.Create();

        var definition = host.DefinitionRepository.GetAll()
            .Single(d => d.Name == ConsumerFeatureContributor.IntFeature);

        Assert.Equal(FeatureValueType.Int, definition.ValueType);
        Assert.Equal("10", definition.DefaultValue);
    }

    [Fact]
    public void Repository_Contains_DefinedName_ReturnsTrue()
    {
        using var host = FeatureManagementTestHost.Create();

        Assert.True(host.DefinitionRepository.Contains(ConsumerFeatureContributor.BooleanFeature));
        Assert.False(host.DefinitionRepository.Contains("App.NotDefined"));
    }

    // ── D2 唯一校验（FeatureDefinitionContext.Add） ──

    [Fact]
    public void Context_Add_DuplicateName_ThrowsInvalidOperationException()
    {
        var context = new FeatureDefinitionContext();
        context.Add(new FeatureDefinition { Name = "Order.NewCheckout" });

        Assert.Throws<InvalidOperationException>(() =>
            context.Add(new FeatureDefinition { Name = "Order.NewCheckout" }));
    }

    [Fact]
    public void Context_Add_EmptyName_ThrowsArgumentException()
    {
        var context = new FeatureDefinitionContext();

        Assert.Throws<ArgumentException>(() =>
            context.Add(new FeatureDefinition { Name = "" }));
    }

    [Fact]
    public void Context_Add_NullName_ThrowsArgumentException()
    {
        var context = new FeatureDefinitionContext();

        Assert.Throws<ArgumentException>(() =>
            context.Add(new FeatureDefinition { Name = null! }));
    }

    [Fact]
    public void Context_Add_Valid_CollectsIntoDefinitions()
    {
        var context = new FeatureDefinitionContext();
        context.Add(new FeatureDefinition { Name = "A", ValueType = FeatureValueType.Boolean, DefaultValue = "true" });
        context.Add(new FeatureDefinition { Name = "B", ValueType = FeatureValueType.String });

        Assert.Equal(2, context.Definitions.Count);
        Assert.Equal(FeatureValueType.Boolean, context.Definitions[0].ValueType);
        Assert.Equal("true", context.Definitions[0].DefaultValue);
        Assert.Equal("B", context.Definitions[1].Name);
    }

    // ── 定义默认值语义 ──

    [Fact]
    public void Definition_DefaultValueType_IsBoolean()
    {
        var definition = new FeatureDefinition { Name = "Default.Type" };

        Assert.Equal(FeatureValueType.Boolean, definition.ValueType);
        Assert.Null(definition.DefaultValue);
    }
}
