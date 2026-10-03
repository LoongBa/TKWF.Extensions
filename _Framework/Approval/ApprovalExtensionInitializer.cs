using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Approval;

/// <summary>
/// Approval 扩展初始化器——经 <c>[TKWFExtension]</c> 被 SG1 发现，三钩子接线：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——注册三标准门面（AddConstructibleService：接口可构造守卫工厂 +
///       实现类 throw-factory）+ IApprovalAssigneeResolver/DefaultApprovalAssigneeResolver（TryAddEnumerable，消费方可追加自定义 resolver）</item>
/// <item>ConfigureFilters——不调用（V0.1.0 无过滤器）</item>
/// <item>InitializeAsync——不调用（V0.1.0 无种子数据）</item>
/// </list>
/// <para>V0.3.0（V4.10.53 ADR90 领域自治根治，正确路线——注册形态三态）：</para>
/// <list type="bullet">
/// <item><b>标准门面</b>——IApprovalService/ApprovalManager、IApprovalQueryService/ApprovalQueryService、
///       IApprovalTimeoutService/ApprovalTimeoutService（接口已标 <c>: IDomainService</c>，实现继承 <see cref="DomainServiceBase"/>
///       经基类 <c>User</c> 取上下文，IDomainUser 永不注册 DI）：<c>AddConstructibleService&lt;接口, 实现&gt;</c>——
///       接口注册为 CurrentAopUser 守卫工厂（域作用域外解析即抛），实现类注册为 throw-factory；消费方统一经
///       <c>User.Use&lt;接口&gt;()</c> 解析（AOP 路径）。</item>
/// <item><b>多实现集合（T3 边界保留）</b>——IApprovalAssigneeResolver/DefaultApprovalAssigneeResolver（纯逻辑、ctor 无 IDomainUser）：
///       <c>TryAddEnumerable</c> 注册（消费方可追加自定义 resolver 扩展 Role→用户解析）；多实现集合经
///       <c>User.Use&lt;IApprovalAssigneeResolver&gt;()</c> 接口 AOP 解析（GetRequiredService 取集合注册——消费方自定义覆盖语义 T3 候选，
///       本批不动）。</item>
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
    /// <para>三标准门面 AddConstructibleService（接口守卫工厂 + 实现 throw-factory——消费方经 User.Use&lt;接口&gt;() AOP 解析）；
    /// Resolver 保持 TryAddEnumerable（多实现集合——消费方可 TryAddEnumerable 追加自定义 resolver 覆盖默认 Role 解析）。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // 审批人解析器——TryAddEnumerable：多实现集合，消费方可 TryAddEnumerable 注册自定义 resolver 扩展（Role→用户解析）
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IApprovalAssigneeResolver, DefaultApprovalAssigneeResolver>());

        // V0.3.0（领域自治根治）：三标准门面注册形态由 TryAddScoped 改 AddConstructibleService——
        // 接口可构造守卫工厂（CurrentAopUser 守卫，域作用域外解析即抛）+ 实现类 throw-factory。
        // 旧形态 TryAddScoped + ctor(IDomainUser)（IDomainUser 永不注册 DI——D01）→ 运行时解析必失败（v0.3.3 同根缺陷）。
        // 消费方统一经 User.Use<IApprovalService>() / User.Use<IApprovalQueryService>() / User.Use<IApprovalTimeoutService>() 解析。
        services.AddConstructibleService<IApprovalService, ApprovalManager>();
        services.AddConstructibleService<IApprovalQueryService, ApprovalQueryService>();
        services.AddConstructibleService<IApprovalTimeoutService, ApprovalTimeoutService>();
    }
}
