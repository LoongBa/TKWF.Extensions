using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Approval;

/// <summary>
/// Approval 扩展初始化器——经 <c>[TKWFExtension]</c> 被 SG1 发现，三钩子接线：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——注册 IApprovalService/ApprovalManager + IApprovalQueryService/ApprovalQueryService
///       + IApprovalAssigneeResolver/DefaultApprovalAssigneeResolver（TryAddEnumerable，消费方可覆盖）</item>
/// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
/// <item>InitializeAsync——不调用（V0.1.0 无种子数据）</item>
/// </list>
/// </summary>
[TKWFExtension("Approval")]
public class ApprovalExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "Approval";

    /// <summary>扩展描述。</summary>
    public override string Description => "轻量审批引擎（三实体模型 + 内置自建状态机 + 审批任务 + 完成事件回调 + 查询门面）";

    /// <summary>
    /// 注册审批引擎服务。
    /// <para>TryAddScoped/TryAddEnumerable：消费方可自定义实现覆盖默认（如 Role→用户 resolver）。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // 审批人解析器——TryAddEnumerable：消费方可 TryAddEnumerable 注册自定义 resolver 覆盖默认
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IApprovalAssigneeResolver, DefaultApprovalAssigneeResolver>());

        // 审批引擎（Scoped）
        services.TryAddScoped<ApprovalManager>();
        services.TryAddScoped<IApprovalService>(sp => sp.GetRequiredService<ApprovalManager>());

        // 审批查询（Scoped）
        services.TryAddScoped<ApprovalQueryService>();
        services.TryAddScoped<IApprovalQueryService>(sp => sp.GetRequiredService<ApprovalQueryService>());

        // v0.2.0：超时处理服务（P10——新实体 DataService 依赖 SG1 自动注册，不显式 TryAddScoped；仅注册超时服务）
        services.TryAddScoped<IApprovalTimeoutService, ApprovalTimeoutService>();
    }
}
