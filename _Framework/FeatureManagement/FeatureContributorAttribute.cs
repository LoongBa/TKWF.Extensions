using System;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// Feature 贡献者标记特性（纯标记，无载荷）——SG1 编译期收集（主框架 V4.9.114 扩展机制）。
/// 业务模块标注后，贡献者在宿主启动时被实例化并调用 <see cref="IFeatureDefinitionContributor.Define"/>。
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FeatureContributorAttribute : Attribute { }
