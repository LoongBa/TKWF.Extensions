namespace TKWF.Ext.FeatureManagement;

/// <summary>Feature 定义贡献者（业务模块声明 Feature 定义，对齐 <c>IPermissionDefinitionContributor</c>）。</summary>
public interface IFeatureDefinitionContributor
{
    /// <summary>向上下文声明 Feature 定义。</summary>
    void Define(FeatureDefinitionContext context);
}
