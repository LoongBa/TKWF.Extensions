using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval;

/// <summary>抄送记录实体——审批发起/完成时通知相关人（不参与审批）。     <para>StartAsync 带 ccUserIds / AddCCAsync 记录；实例提交/终态时发布事件，投递组装消费方 Notifications。     索引 IX_ac_instance（InstanceId，非唯一）。</para></summary>
public partial class ApprovalCCEntity
{
    /// <summary>
    /// 根据需要添加业务验证逻辑 (例如跨表验证、状态机检查) 
    /// </summary>
    partial void OnBusinessValidate(EnumSceneFlags scene, List<ValidationResult> results)
    {
        // 示例：领域驱动设计的业务验证规则 
        // if (this.Status == Status.Disabled && this.Stock > 0)
        //     results.Add(new ValidationResult("禁用状态下不能有库存", new[] { nameof(Status) }));
    }
}