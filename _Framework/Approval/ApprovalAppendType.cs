namespace TKWF.Ext.Approval;

/// <summary>
/// 加签类型——Before（前加签）/ After（后加签）。
/// <para>P8 评审：v0.2.0 公开 API 不暴露（<see cref="IApprovalService.AppendApproverAsync"/> 不接收此参数）——
/// 统一为「当前步骤并行追加审批人」，Before/After 行为一致（钉钉 before/after 是流程编排语义，轻量引擎避免）。
/// 类型为 public 仅因 <see cref="ApprovalAppendEntity.AppendType"/> 实体列需公开（C# 可访问性约束），
/// 非公开 API 参数。v0.2.0 记录固定为 After。</para>
/// </summary>
public enum ApprovalAppendType
{
    /// <summary>前加签（钉钉语义：审批人之前追加——v0.2.0 行为与 After 一致）。</summary>
    Before = 0,

    /// <summary>后加签（钉钉语义：审批人之后追加——v0.2.0 默认记录值）。</summary>
    After = 1
}

/// <summary>
/// 加签模式——Participate（参与审批）/ Notify（仅通知）。
/// </summary>
public enum ApprovalAppendMode
{
    /// <summary>参与审批——创建新审批任务（加签人走既有 ApproveAsync，步骤判定复用 Any/All）。</summary>
    Participate = 0,

    /// <summary>仅通知——记录加签 + 发布 <see cref="ApprovalAppendNotifyEvent"/>（不建任务）。</summary>
    Notify = 1
}
