using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 微信公众号运营 Feature 定义名常量（V0.4.0——消息业务总开关 DB 化，FeatureManagement 消费）。
/// <para>统一常量承载（P2 单一事实源，防 typo 静默解析错 feature）；消费方/管理 API 写值经
/// <c>IFeatureManager.SetValueAsync(名, "true"/"false", FeatureProviders.Global, null)</c> 同常量。</para>
/// </summary>
public static class WeChatOperationsFeatureNames
{
    /// <summary>
    /// 消息业务总开关（Boolean——默认 <c>true</c>，对齐 V0.2.1 Options <see cref="WeChatOperationsOptions.MessageBusinessEnabled"/>
    /// 默认语义：关闭后本地回复停发统一维护提示 <see cref="WeChatOperationsOptions.DefaultReplyText"/>；<b>转发网关不受控</b>
    /// （<see cref="IMessageGatewayService.RouteAsync"/> 先行仍转发生效）。
    /// <para>运行时切换：<c>manager.SetValueAsync(WeChatOperationsFeatureNames.MessageBusiness, "true"/"false", FeatureProviders.Global, null, ct)</c>
    /// ——DB 权威（<c>FeatureValue</c> 表）+ 版本号缓存失效 + 分布式事件（多实例即时生效）；重启不丢。</para>
    /// </summary>
    public const string MessageBusiness = "WeChatOperations.MessageBusiness";
}

/// <summary>
/// 消息业务总开关 Feature 定义贡献者（V0.4.0——DB 化）：编译期声明 <c>WeChatOperations.MessageBusiness</c>
/// （Boolean，DefaultValue="true" 对齐 Options 默认）。经 FeatureManagement Initializer 编译期接口判定收集
/// （<c>ProjectMetaContext.Contributors[Feature]</c> + <c>CreateContributorInstances</c>——扩展程序集内自动聚合，
/// 零手动注册）；纯定义贡献者（无领域依赖，不继承 DomainServiceBase——可被 CreateContributorInstances 直接实例化）。
/// </summary>
public sealed class WeChatOperationsFeatureDefinitionContributor : IFeatureDefinitionContributor
{
    /// <inheritdoc />
    public void Define(FeatureDefinitionContext context)
    {
        context.Add(new FeatureDefinition
        {
            Name = WeChatOperationsFeatureNames.MessageBusiness,
            DisplayName = "消息业务总开关",
            Description = "公众号本地自动回复总开关——关闭后关键词/欢迎语/兜底等本地回复统一返回维护提示（转发网关不受控）。",
            Group = "WeChatOperations",
            ValueType = FeatureValueType.Boolean,
            DefaultValue = "true",
        });
    }
}