using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批步骤定义——审批流程中一步的描述（StepsJson 序列化载体）。
/// <para>Index 为步骤序号（从 0 开始，顺序执行）；Name 为步骤显示名；
/// ApproverType+ApproverValue 定义审批人来源；Mode 为或签/会签模式；
/// Condition 预留（v0.1.0 无条件跳转，顺序步骤链）。</para>
/// </summary>
/// <param name="Index">步骤序号（从 0 开始，顺序执行）。</param>
/// <param name="Name">步骤显示名（如"部门经理""财务"）。</param>
/// <param name="ApproverType">审批人类型（User/Role）。</param>
/// <param name="ApproverValue">审批人值（User=用户 ID，Role=角色名）。</param>
/// <param name="Mode">审批模式（Any 或签 / All 会签）。</param>
/// <param name="Condition">条件表达式（v0.1.0 预留，null 表示无条件）。</param>
public sealed record ApprovalStepDefinition(
    int Index,
    string Name,
    ApprovalApproverType ApproverType,
    string ApproverValue,
    ApprovalMode Mode = ApprovalMode.Any,
    string? Condition = null);

/// <summary>
/// ApprovalStepDefinition 的 JSON 序列化/反序列化辅助（System.Text.Json）。
/// </summary>
public static class ApprovalStepDefinitionSerializer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>将步骤定义列表序列化为 JSON 字符串（存入 ApprovalFlowEntity.StepsJson）。</summary>
    public static string Serialize(IReadOnlyList<ApprovalStepDefinition> steps)
        => JsonSerializer.Serialize(steps, s_options);

    /// <summary>将 JSON 字符串反序列化为步骤定义列表。</summary>
    public static IReadOnlyList<ApprovalStepDefinition> Deserialize(string stepsJson)
        => JsonSerializer.Deserialize<List<ApprovalStepDefinition>>(stepsJson, s_options)
           ?? throw new JsonException("StepsJson 反序列化结果为 null");
}
