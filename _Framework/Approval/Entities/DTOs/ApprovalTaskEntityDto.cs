using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval.DTOs;

/// <summary>审批任务 DTO 扩展。</summary>
public partial record ApprovalTaskEntityDto
{
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results) { }
}
