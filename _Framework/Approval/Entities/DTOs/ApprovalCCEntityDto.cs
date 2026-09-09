using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TKW.Framework;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval.DTOs;

/// <summary>抄送记录 DTO 扩展。</summary>
public partial record ApprovalCCEntityDto
{
    partial void OnCustomValidate(EnumSceneFlags scene, List<ValidationResult> results) { }
}
