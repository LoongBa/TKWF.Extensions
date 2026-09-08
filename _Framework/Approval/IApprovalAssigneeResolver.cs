using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批人解析器——将步骤定义中的 ApproverType+ApproverValue 解析为实际用户 ID 列表。
/// <para>User 类型直接返回 [ApproverValue]（内置）；Role 类型由消费方注册自定义 resolver 实现。
/// 不跨模块引用实体（数据关联立场）——使用指南提供 Identity UserRoleEntity 实现示例。</para>
/// </summary>
public interface IApprovalAssigneeResolver
{
    /// <summary>
    /// 将步骤审批人定义解析为用户 ID 列表。
    /// <para>User 类型返回 [ApproverValue]（单元素）；Role 类型消费方解析（如查 Identity UserRoleEntity）。</para>
    /// </summary>
    /// <param name="step">审批步骤定义。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析后的用户 ID 列表（会签/或签均为多条任务，每条对应一个用户）。</returns>
    Task<IReadOnlyList<string>> ResolveUserIdsAsync(ApprovalStepDefinition step, CancellationToken ct = default);
}

/// <summary>
/// 默认审批人解析器——User 类型直接返回 [ApproverValue]；Role 类型抛 NotSupportedException（P1-2）。
/// <para>消费方须注册自定义 IApprovalAssigneeResolver 覆盖此默认实现（Role→用户反查属消费方职责）。</para>
/// </summary>
public sealed class DefaultApprovalAssigneeResolver : IApprovalAssigneeResolver
{
    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ResolveUserIdsAsync(ApprovalStepDefinition step, CancellationToken ct = default)
    {
        return step.ApproverType switch
        {
            ApprovalApproverType.User => Task.FromResult<IReadOnlyList<string>>(new[] { step.ApproverValue }),
            ApprovalApproverType.Role => throw new NotSupportedException(
                $"角色→用户解析不支持（ApproverValue={step.ApproverValue}）。请注册自定义 IApprovalAssigneeResolver 实现。"),
            _ => throw new ArgumentOutOfRangeException(nameof(step), $"未知的 ApproverType: {step.ApproverType}")
        };
    }
}
