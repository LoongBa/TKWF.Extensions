using System;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批操作异常——非法状态迁移、身份校验失败、唯一约束冲突等审批业务异常。
/// <para>对齐 PrintTemplates InvalidOperationException 惯例（显式业务异常，便于消费方 catch 精确处理）。</para>
/// </summary>
public sealed class InvalidApprovalOperationException : InvalidOperationException
{
    public InvalidApprovalOperationException(string message) : base(message) { }
    public InvalidApprovalOperationException(string message, Exception innerException) : base(message, innerException) { }
}
