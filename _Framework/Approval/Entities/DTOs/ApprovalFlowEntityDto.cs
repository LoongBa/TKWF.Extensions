using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval.DTOs;

/// <summary>审批流定义 DTO 扩展（SG1 生成基础，此 partial 编写自定义验证）。</summary>
public partial record ApprovalFlowEntityDto
{
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results) { }
}
